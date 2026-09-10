using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TradingJournal.Data;
using TradingJournal.Services.Valuation;

namespace TradingJournal.Tests;
public sealed class AaplValuationV2Tests
{
    [Fact]
    public async Task PublishedFinancialStatements_ParseCalculatePersistAndReconcile()
    {
        using var facts=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","aapl-fy2025-facts.json")));
        var source=facts.RootElement.GetProperty("_provenance").GetProperty("source").GetString()!;
        var dataset=SecFinancialParser.Parse(facts.RootElement,source,new(2026,9,10));
        var p=dataset.Latest!;
        Assert.Equal(416161000000m,p.Facts["Revenue"].Value);
        Assert.Equal(133050000000m,p.Facts["Ebit"].Value);
        Assert.Equal(11698000000m,p.Facts["Da"].Value);
        Assert.Equal(12715000000m,p.Facts["CapEx"].Value);
        Assert.Equal(98657000000m,ValuationMath.Debt(p));
        Assert.Equal(96486000000m,p.Facts["ShortInvestments"].Value+p.Facts["LongInvestments"].Value);
        Assert.Equal(15004697000m,p.Facts["DilutedShares"].Value);
        var r=ValuationV2Tests.Engine().Calculate("AAPL","Technology",dataset,315.33999634m,new(2026,9,9),new(2026,9,10));
        Assert.Equal(3,r.Scenarios.Count);
        var b=r.Scenarios.Single(x=>x.Name=="Base");
        // Independent reconciliation of projected FCFF and terminal reinvestment, not a target fair value.
        var pv=b.Years.Sum(y=>(y.Ebit*(1-y.TaxRate)+y.Da-y.CapEx-y.ChangeNwc)/(decimal)Math.Pow((double)(1+b.Wacc),y.Year));
        var tv=b.Years[^1].Ebit*(1-b.Years[^1].TaxRate)*(1+b.TerminalGrowth)*(1-b.TerminalGrowth/b.TerminalRoic)/(b.Wacc-b.TerminalGrowth);
        var value=(pv+tv/(decimal)Math.Pow((double)(1+b.Wacc),10)-b.Debt+b.ExcessCash+b.Investments)/b.Shares;
        Assert.InRange(Math.Abs(value-b.FairValue),0,.000001m);
        await using var cn=new SqliteConnection("Data Source=:memory:");await cn.OpenAsync();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(cn).Options);
        await db.Database.EnsureCreatedAsync();var store=new ValuationPersistence(db);
        await store.SaveAsync(r,dataset,default);
        Assert.Equal(b.FairValue,(await store.ReadAsync(r.Id,default))!.BaseFairValue);
        Assert.Equal(98657000000m,ValuationMath.Debt((await store.InputAsync(r.InputHash,default))!.Latest!));
        var output=Environment.GetEnvironmentVariable("VALUATION_AUDIT_OUTPUT");
        if(!string.IsNullOrWhiteSpace(output)) await File.WriteAllTextAsync(output,ValuationJson.Serialize(new {report=r,financials=dataset}));
    }
}
