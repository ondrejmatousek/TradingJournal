namespace TradingJournal.Services.Screening;

public static class RelativeValuationCalculator
{
    public static decimal? FairValue(FundamentalInput f, string sector, decimal? historicalPe = null)
    {
        var eps = f.NetIncome / Math.Max(.01m, f.Shares);
        if (eps <= 0) return null;
        var pe = historicalPe is > 3 and < 60 ? historicalPe.Value : sector switch { "Technology" or "Communication Services" => 22m, "Healthcare" => 18m, "Consumer Defensive" => 20m, "Energy" => 12m, _ => 16m };
        return eps * pe;
    }
}

public static class FcfValuationCalculator
{
    public static decimal? FairValue(FundamentalInput f, decimal requiredYield = .06m)
    {
        var fcf = f.OperatingCashFlow - f.CapitalExpenditure;
        return fcf > 0 && f.Shares > 0 && requiredYield > 0 ? fcf / f.Shares / requiredYield : null;
    }
}

public static class EnterpriseValuationCalculator
{
    public static decimal? FairValue(FundamentalInput f, string sector)
    {
        if (f.Ebitda is not > 0 || f.Shares <= 0) return null;
        var multiple = sector switch
        {
            "Technology" or "Communication Services" => 16m,
            "Healthcare" => 14m,
            "Consumer Defensive" => 13m,
            "Energy" => 7m,
            "Industrials" => 12m,
            _ => 10m
        };
        return (f.Ebitda.Value * multiple + (f.CashAndEquivalents ?? 0m) - (f.TotalDebt ?? 0m)) / f.Shares;
    }
}

public static class ForwardEarningsValuationCalculator
{
    public static decimal? FairValue(FundamentalInput f, string sector)
    {
        if (f.ForwardEps is not > 0) return null;
        var multiple = sector switch
        {
            "Technology" or "Communication Services" => 20m,
            "Healthcare" => 17m,
            "Consumer Defensive" => 18m,
            "Energy" => 10m,
            _ => 14m
        };
        return f.ForwardEps.Value * multiple;
    }
}
