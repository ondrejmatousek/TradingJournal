namespace TradingJournal.Services.Valuation;

public static class PriceLabMath
{
    // FCFF is never discounted directly at a shareholder's required return.
    // Here debt stays constant in nominal terms and is refinanced; net borrowing is zero.
    // FCFE = FCFF - after-tax interest. Terminal interest is a constant perpetuity,
    // while operating FCFF grows at g. Debt is therefore NOT subtracted a second time.
    public static decimal? ReturnPrice(ValuationReport report, ValuationScenario scenario, decimal requiredReturn)
    {
        if (requiredReturn <= scenario.TerminalGrowth || requiredReturn <= 0 || scenario.Shares <= 0) return null;
        var kd = report.Assumptions.FirstOrDefault(a => a.Name == "CostOfDebt")?.Value;
        var tax = report.Assumptions.FirstOrDefault(a => a.Name == "Tax")?.Value;
        if (kd == null || tax == null || tax is < 0 or >= 1) return null;
        var interest = scenario.Debt * kd.Value * (1 - tax.Value);
        var explicitPv = scenario.Years.Sum(y => ValuationMath.Pv(y.Fcff - interest, requiredReturn, y.Year));
        var terminal = scenario.TerminalFcff / (requiredReturn - scenario.TerminalGrowth) - interest / requiredReturn;
        var equity = explicitPv + ValuationMath.Pv(terminal, requiredReturn, scenario.Years.Count)
            + scenario.ExcessCash + scenario.Investments - scenario.OtherClaims;
        return equity > 0 ? equity / scenario.Shares : null;
    }

    public static ReverseDcfResult Reverse(decimal price, Func<decimal, ValuationScenario?> scenarioAtGrowth, CancellationToken ct = default)
    {
        if (price <= 0) return new("unavailable", null, null, "Chybí kladná cena.");
        var intervals = new List<(decimal Low, decimal High)>();
        var exact = new List<(decimal Growth, ValuationScenario Scenario)>();
        decimal? previousGrowth = null, previousDifference = null;
        var invalid = false;
        for (var i = 0; i <= 30; i++)
        {
            ct.ThrowIfCancellationRequested();
            var growth = -.10m + i * .02m;
            var scenario = scenarioAtGrowth(growth);
            if (scenario == null) { previousGrowth = previousDifference = null; invalid = true; continue; }
            var difference = scenario.FairValue - price;
            if (Math.Abs(difference) <= .000001m)
            {
                exact.Add((growth, scenario)); previousGrowth = previousDifference = null; continue;
            }
            if (previousDifference != null && Math.Sign(previousDifference.Value) != Math.Sign(difference))
                intervals.Add((previousGrowth!.Value, growth));
            previousGrowth = growth; previousDifference = difference;
        }
        var solutions = intervals.Count + exact.Count;
        if (solutions != 1) return new(solutions > 1 ? "ambiguous" : invalid ? "unavailable" : "outside-range", null, null,
            solutions > 1 ? "Více řešení při stejných předpokladech; růst nelze jednoznačně určit."
            : "Cena nemá ověřitelné řešení v rozsahu počátečního růstu −10 až 50 %. Modelový rozsah není předpověď.");
        if (exact.Count == 1) return Found(exact[0].Growth, exact[0].Scenario);
        var (low, high) = intervals[0];
        var lowValue = scenarioAtGrowth(low)!.FairValue - price;
        for (var i = 0; i < 45; i++)
        {
            ct.ThrowIfCancellationRequested();
            var mid = (low + high) / 2;
            var scenario = scenarioAtGrowth(mid);
            if (scenario == null) return new("unavailable", null, null, "V okolí řešení model neposkytuje platné scénáře.");
            var difference = scenario.FairValue - price;
            if (Math.Abs(difference) <= Math.Max(.000001m, price * .000001m)) return Found(mid, scenario);
            if (Math.Sign(difference) == Math.Sign(lowValue)) { low = mid; lowValue = difference; } else high = mid;
        }
        return new("unavailable", null, null, "Numerické řešení nedosáhlo požadované přesnosti.");
    }

    private static ReverseDcfResult Found(decimal growth, ValuationScenario scenario) => new("solved", growth * 100,
        scenario.Years[^1].Revenue, "Počáteční roční růst tržeb, který při daných maržích, reinvesticích a WACC vysvětluje dnešní cenu. Postupně klesá k terminálnímu růstu v desátém roce; nejde o desetileté CAGR ani odhad analytiků.");
}
