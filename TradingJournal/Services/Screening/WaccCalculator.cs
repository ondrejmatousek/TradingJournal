namespace TradingJournal.Services.Screening;

public sealed record WaccAssumptions(decimal RiskFreeRate, decimal Beta, decimal EquityRiskPremium,
    decimal CostOfDebt, decimal TaxRate, decimal EquityWeight, decimal DebtWeight, bool UsedFallback);

public static class WaccCalculator
{
    public static WaccAssumptions Calculate(decimal? treasuryYieldPercent, decimal? beta = null,
        decimal? costOfDebt = null, decimal? taxRate = null, decimal? debtWeight = null)
    {
        var riskFree = treasuryYieldPercent is >= 0 and <= 15 ? treasuryYieldPercent.Value / 100m : .045m;
        var b = beta is > 0 and < 5 ? beta.Value : 1m;
        var debt = costOfDebt is >= 0 and <= 30 ? costOfDebt.Value / 100m : .05m;
        var tax = taxRate is >= 0 and <= 60 ? taxRate.Value / 100m : .21m;
        var dw = debtWeight is >= 0 and <= 90 ? debtWeight.Value / 100m : .10m;
        var ew = 1m - dw;
        return new(riskFree, b, .05m, debt, tax, ew, dw, treasuryYieldPercent is null || beta is null || costOfDebt is null || taxRate is null || debtWeight is null);
    }

    public static decimal Rate(WaccAssumptions a)
        => Math.Clamp(a.EquityWeight * (a.RiskFreeRate + a.Beta * a.EquityRiskPremium)
            + a.DebtWeight * a.CostOfDebt * (1m - a.TaxRate), .09m, .20m);
}
