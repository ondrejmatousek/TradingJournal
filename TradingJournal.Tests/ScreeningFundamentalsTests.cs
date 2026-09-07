using System.Text.Json;
using TradingJournal.Services.Screening;

namespace TradingJournal.Tests;
public class ScreeningFundamentalsTests
{
    private static JsonDocument Fixture(bool capex, bool futureShares = false)
    {
        object Annual(decimal value, string start="2025-01-01",string end="2025-12-31") => new{start,end,filed="2026-02-15",val=value};
        object Tag(params object[] entries) => new{units=new Dictionary<string,object>{{"USD",entries}}};
        var gaap=new Dictionary<string,object>{
            ["Revenues"]=Tag(Annual(1000,"2024-01-01","2024-12-31"),Annual(1100),Annual(999999,"2025-10-01")),
            ["NetCashProvidedByUsedInOperatingActivities"]=Tag(Annual(250)),["NetIncomeLoss"]=Tag(Annual(150)),
            ["Assets"]=Tag(new{end="2025-12-31",filed="2026-02-15",val=1500}),["Liabilities"]=Tag(new{end="2025-12-31",filed="2026-02-15",val=500})};
        if(capex)gaap["PaymentsToAcquirePropertyPlantAndEquipment"]=Tag(Annual(50));
        return JsonDocument.Parse(JsonSerializer.Serialize(new{facts=new Dictionary<string,object>{["us-gaap"]=gaap,["dei"]=new{EntityCommonStockSharesOutstanding=new{units=new Dictionary<string,object>{["shares"]=new[]{new{end="2026-08-01",filed=futureShares?"2027-01-01":"2026-08-15",val=100}}}}}}}));
    }
    [Fact]
    public void Parser_UsesMatchingAnnualPeriodsNotQuarterlyFacts()
    {
        using var json=Fixture(true);
        var result=ScreeningDataProvider.ParseFundamentals(json.RootElement,"https://www.sec.gov/",new(2026,9,6));
        Assert.NotNull(result);Assert.Equal(1100,result.Revenue);Assert.Equal(1000,result.PreviousRevenue);Assert.Equal(50,result.CapitalExpenditure);
    }
    [Fact]
    public void Parser_DoesNotAssumeMissingCapexIsZeroOrUseFutureFilings()
    {
        using var missing=Fixture(false);using var future=Fixture(true,true);
        Assert.Null(ScreeningDataProvider.ParseFundamentals(missing.RootElement,"https://www.sec.gov/",new(2026,9,6)));
        Assert.Null(ScreeningDataProvider.ParseFundamentals(future.RootElement,"https://www.sec.gov/",new(2026,9,6)));
    }
}
