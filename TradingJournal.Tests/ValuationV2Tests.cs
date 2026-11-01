using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TradingJournal.Data;
using TradingJournal.Services.Valuation;

namespace TradingJournal.Tests;

public sealed class ValuationV2Tests
{
    public static FinancialDataset Dataset()
    {
        var periods=new List<FinancialPeriod>();
        for(var y=2020;y<=2025;y++)
        {
            var start=new DateOnly(y,1,1);var end=new DateOnly(y,12,31);var rev=1000m*(decimal)Math.Pow(1.06,y-2020);
            var vals=new Dictionary<string,decimal>{{"Revenue",rev},{"Ebit",rev*.25m},{"Da",rev*.04m},{"CapEx",rev*.05m},
                {"Tax",rev*.05m},{"Pretax",rev*.25m},{"NetIncome",rev*.20m},{"Ocf",rev*.24m},{"Receivables",rev*.12m},
                {"Inventory",rev*.03m},{"Payables",rev*.08m},{"Cash",100},{"LongDebt",100},{"CurrentDebt",20},{"CommercialPaper",10},
                {"BasicShares",100},{"DilutedShares",102},{"Sbc",10}};
            var facts=vals.ToDictionary(x=>x.Key,x=>new FinancialFact(x.Value,x.Key.EndsWith("Shares")?"shares":"USD",start,end,new(y+1,2,15),"https://example.com/fixture",x.Key,"fixture"));
            periods.Add(new(start,end,false,facts));
        }
        return new("USD",new(2026,3,1),periods,periods[^1],new(100,"shares",new(2026,2,1),new(2026,2,1),new(2026,2,15),"https://example.com/fixture","EntityCommonStockSharesOutstanding","fixture"),[]);
    }
    public static IValuationEngine Engine(ValuationOptions? o=null)=>new ValuationEngine(Options.Create(o??new()),new FinancialNormalizationService(),new WaccCalculator());
    private static ValuationReport Run(FinancialDataset? d=null,string sector="Technology",ValuationOptions? o=null)
        =>Engine(o).Calculate("TEST",sector,d??Dataset(),15,new(2026,2,28),new(2026,3,1));
    [Fact] public void FcffAndNopatHaveCorrectSigns()
    {Assert.Equal(75,ValuationMath.Nopat(100,.25m));Assert.Equal(50,ValuationMath.Fcff(75,10,20,15));}
    [Fact] public void WaccUsesMarketWeightsWithoutNinePercentFloor()
    {Assert.Equal(.0775m,new WaccCalculator().Calculate(.03m,1,.05m,.04m,.25m,950,50));}
    [Theory] [InlineData(.02,.02)] [InlineData(.01,.02)]
    public void InvalidTerminalSpreadIsRejected(double w,double g)=>Assert.Throws<ArgumentOutOfRangeException>(()=>ValuationMath.Terminal(100,(decimal)g,.10m,(decimal)w));
    [Fact] public void TerminalGrowthRequiresReinvestment()
    {Assert.Equal(80m/.06m,ValuationMath.Terminal(100,.02m,.10m,.08m));}
    [Fact] public void DebtDoesNotDoubleCountAggregateOrCommercialPaper()
    {
        var p=Dataset().Latest!;p.Facts["DebtAggregate"]=p.Facts["LongDebt"] with {Value=120};
        p.Facts["ShortDebt"]=p.Facts["LongDebt"] with {Value=15};Assert.Equal(135,ValuationMath.Debt(p));
    }
    [Fact] public void NwcExcludesCashAndDebt()
    {var p=Dataset().Latest!;var before=ValuationMath.Nwc(p);p.Facts["Cash"]=p.Facts["Cash"] with{Value=99999};Assert.Equal(before,ValuationMath.Nwc(p));}
    [Theory] [InlineData("LongDebt")] [InlineData("CurrentDebt")] [InlineData("CapEx")] [InlineData("Ebit")] [InlineData("Da")] [InlineData("Cash")]
    public void MissingRequiredInputNeverProducesLegacyValue(string key)
    {var d=Dataset();d.Latest!.Facts.Remove(key);Assert.Null(Run(d).BaseFairValue);}
    [Fact] public void MissingAllShareConceptsPreventsValuation()
    {var d=Dataset() with{ActualShares=null};d.Latest!.Facts.Remove("DilutedShares");Assert.Null(Run(d).BaseFairValue);}
    [Fact] public void WeightedDilutedFallbackIsExplicit()
    {var r=Run(Dataset() with{ActualShares=null});Assert.NotNull(r.BaseFairValue);Assert.Contains("fallback",r.ShareType);}
    [Fact] public void PossibleUnreconciledSplitIsRejected()
    {var d=Dataset();d=d with{ActualShares=d.ActualShares! with{Value=400}};Assert.Equal("InvalidData",Run(d).Status);}
    [Fact] public void InvalidCurrencyUnitIsRejected()
    {var d=Dataset();d.Latest!.Facts["Revenue"]=d.Latest.Facts["Revenue"] with{Unit="USD millions"};Assert.Equal("InvalidData",Run(d).Status);}
    [Theory] [InlineData("Financial Services")] [InlineData("Bank")] [InlineData("Real Estate")] [InlineData("REIT")]
    public void UnsupportedStrategiesNeverReturnIndustrialDcf(string sector)
    {var r=Run(sector:sector);Assert.Equal("UnsupportedCompanyType",r.Status);Assert.Empty(r.Scenarios);}
    [Fact] public void AllForecastsAndBridgesReconcileIndependently()
    {
        var r=Run();Assert.Equal(3,r.Scenarios.Count);
        foreach(var s in r.Scenarios)
        {
            Assert.Equal(10,s.Years.Count);
            foreach(var y in s.Years)
            {
                Assert.Equal(y.Ebit*(1-y.TaxRate)+y.Da-y.CapEx-y.ChangeNwc,y.Fcff);
                Assert.InRange(Math.Abs(y.Fcff/(decimal)Math.Pow((double)(1+s.Wacc),y.Year)-y.PresentValueFcff),0,.000000001m);
            }
            Assert.Equal(s.PvExplicit+s.PvTerminal,s.EnterpriseValue);
            Assert.Equal(s.EnterpriseValue-s.Debt+s.ExcessCash+s.Investments-s.OtherClaims,s.EquityValue);
            Assert.Equal(s.EquityValue/s.Shares,s.FairValue);
            Assert.Equal(s.TerminalNopat*s.TerminalGrowth/s.TerminalRoic,s.TerminalReinvestment);
        }
        Assert.True(r.Scenarios[0].FairValue<r.Scenarios[1].FairValue);
        Assert.True(r.Scenarios[1].FairValue<r.Scenarios[2].FairValue);
        Assert.Equal(r.BaseFairValue*(1-r.MarginOfSafety),r.BuyBelow);
        Assert.Equal(25,r.Sensitivity.Count);Assert.InRange(r.DataQuality,1,99);
        var b=r.Scenarios[1];Assert.Equal(b.FairValue,r.Sensitivity.Single(c=>c.Wacc==b.Wacc&&c.Growth==b.TerminalGrowth).FairValue);
    }
    [Fact] public void HigherDiscountRateReducesValue()
    {Assert.True(Run(o:new(){Erp=.07m}).BaseFairValue<Run().BaseFairValue);}
    [Fact] public void UnknownReferenceMultiplesAreNotFabricated()
    {Assert.All(Run().CrossChecks,c=>Assert.Null(c.ImpliedValue));}
    [Fact] public async Task ImmutablePersistenceAndDynamicTickerCatalog()
    {
        await using var cn=new SqliteConnection("Data Source=:memory:");await cn.OpenAsync();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(cn).Options);
        await db.Database.EnsureCreatedAsync();var store=new ValuationPersistence(db);var r=Run();
        await store.SaveAsync(r,Dataset(),default);await store.SaveAsync(r,Dataset(),default);
        Assert.Equal(1,await db.Set<ValuationRunEntity>().CountAsync());
        Assert.Equal(r.BaseFairValue,(await store.ReadAsync(r.Id,default))!.BaseFairValue);
        db.Add(new InvestmentListing{Symbol="NEWCO",Name="New company",Sector="Industrials"});await db.SaveChangesAsync();
        Assert.Contains(await new InvestmentCatalog(db).AllAsync(default),x=>x.Symbol=="NEWCO");
        Assert.NotNull(Engine().Calculate("NEWCO","Industrials",Dataset(),15,new(2026,2,28),new(2026,3,1)).BaseFairValue);
    }
    [Fact] public void ParserBuildsTtmFromCumulativeYtdNotFourOverlappingRows()
    {
        object Fact(decimal v,string s,string e,string filed)=>new{val=v,start=s,end=e,filed,accn="fixture"};
        object Tag(params object[] rows)=>new{units=new Dictionary<string,object>{{"USD",rows}}};
        var rows=new[]{Fact(100,"2024-01-01","2024-12-31","2025-02-01"),Fact(120,"2025-01-01","2025-12-31","2026-02-01"),Fact(50,"2025-01-01","2025-06-30","2025-08-01"),Fact(70,"2026-01-01","2026-06-30","2026-08-01")};
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{facts=new Dictionary<string,object>{{"us-gaap",new Dictionary<string,object>{{"Revenues",Tag(rows)},{"NetCashProvidedByUsedInOperatingActivities",Tag(rows)}}}}}));
        var d=SecFinancialParser.Parse(json.RootElement,"https://example.com",new(2026,9,1));
        Assert.True(d.Latest!.IsTtm);Assert.Equal(140,d.Latest.Facts["Revenue"].Value);Assert.Equal(140,d.Latest.Facts["Ocf"].Value);
        Assert.Contains("TTM FY+YTD",d.Latest.Facts["Ocf"].Derivation);
    }
    [Fact] public void ParserKeepsCompleteFyWhenTtmDropsPreviouslyAvailableFact()
    {
        object Fact(decimal v,string s,string e,string filed)=>new{val=v,start=s,end=e,filed,accn="fixture"};
        object Tag(params object[] rows)=>new{units=new Dictionary<string,object>{{"USD",rows}}};
        var revenue=new[]{Fact(100,"2024-01-01","2024-12-31","2025-02-01"),Fact(40,"2024-01-01","2024-06-30","2024-08-01"),Fact(50,"2025-01-01","2025-06-30","2025-08-01"),Fact(70,"2026-01-01","2026-06-30","2026-08-01")};
        var capex=new[]{Fact(20,"2024-01-01","2024-12-31","2025-02-01")};
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{facts=new Dictionary<string,object>{{"us-gaap",new Dictionary<string,object>{{"Revenues",Tag(revenue)},{"PaymentsToAcquirePropertyPlantAndEquipment",Tag(capex)}}}}}));
        var d=SecFinancialParser.Parse(json.RootElement,"https://example.com",new(2026,9,1));
        Assert.False(d.Latest!.IsTtm);Assert.Contains("TTM incomplete",d.Warnings.Single());
        Assert.True(d.Latest.Facts.ContainsKey("CapEx"));
    }

    [Fact] public void ParserDerivesEbitWhenIssuerOmitsOperatingIncomeTag()
    {
        object Fact(decimal v,string s,string e,string filed)=>new{val=v,start=s,end=e,filed,accn="fixture"};
        object Tag(params object[] rows)=>new{units=new Dictionary<string,object>{{"USD",rows}}};
        var revenue=new[]{Fact(100,"2025-01-01","2025-12-31","2026-02-01")};
        var pretax=new[]{Fact(36,"2025-01-01","2025-12-31","2026-02-01")};
        var interest=new[]{Fact(7,"2025-01-01","2025-12-31","2026-02-01")};
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(new{facts=new Dictionary<string,object>{{"us-gaap",new Dictionary<string,object>
        {{"Revenues",Tag(revenue)},
          {"IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest",Tag(pretax)},
          {"InterestExpenseNonOperating",Tag(interest)}}}}}));
        var d=SecFinancialParser.Parse(json.RootElement,"https://example.com",new(2026,9,1));
        Assert.Equal(43m,d.Latest!.Facts["Ebit"].Value);
        Assert.Equal("DerivedEbit",d.Latest.Facts["Ebit"].Tag);
        Assert.Contains("Pretax income + positive interest",d.Latest.Facts["Ebit"].Derivation);
    }
}
