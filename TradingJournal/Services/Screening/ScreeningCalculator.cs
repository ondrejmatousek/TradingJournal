using TradingJournal.Models;
using System.Text.Json;

namespace TradingJournal.Services.Screening;

public static class ScreeningCalculator
{
    private static readonly JsonSerializerOptions HistoryJson = new() { PropertyNameCaseInsensitive = true };
    public static decimal Median(IEnumerable<decimal> values)
    {
        var a = values.Order().ToArray();
        return a.Length % 2 == 0 ? (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2 : a[a.Length / 2];
    }

    public static SeasonalWindow Seasonality(IReadOnlyList<MarketPrice> prices, DateOnly asOf, int days)
    {
        var sorted = prices.Where(p => p.Date <= asOf && p.Close > 0).DistinctBy(p => p.Date).OrderBy(p => p.Date).ToArray();
        var samples = new List<(int Year, decimal Return)>();
        for (var year = asOf.Year - 15; year < asOf.Year; year++)
        {
            var start = new DateOnly(year, asOf.Month, Math.Min(asOf.Day, DateTime.DaysInMonth(year, asOf.Month)));
            var index = Array.FindIndex(sorted, p => p.Date >= start);
            if (index < 0 || sorted[index].Date.DayNumber - start.DayNumber > 7 || index + days >= sorted.Length) continue;
            var end = sorted[index + days];
            // Reject incomplete windows and interrupted histories (delisting or missing trading sessions).
            if (end.Date >= asOf || end.Date.DayNumber - sorted[index].Date.DayNumber > days * 2 + 10) continue;
            samples.Add((year, (end.Close / sorted[index].Close - 1) * 100));
        }
        if (samples.Count < 5) return new(days, samples.Count, null, null, null, null);
        var recent = samples.Where(s => s.Year >= asOf.Year - 5).Select(s => s.Return).ToArray();
        return new(days, samples.Count, Median(samples.Select(s => s.Return)), samples.Count(s => s.Return > 0) * 100m / samples.Count,
            samples.Min(s => s.Return), recent.Length >= 3 ? Median(recent) : null);
    }

    public static ValuationResult? Value(FundamentalInput f, string sector, decimal? treasuryYield = null, decimal? historicalPe = null)
    {
        // This is deliberately conservative and transparent. It is a model estimate, not a price target.
        if (sector is "Financial Services" or "Real Estate" || f.Currency != "USD" || f.Shares <= 0 || f.PreviousRevenue <= 0 || f.Revenue <= 0) return null;
        var cash = f.OperatingCashFlow - f.CapitalExpenditure;
        if (cash <= 0 || f.CapitalExpenditure < 0) return null;
        // Mean reversion and guardrails prevent one exceptional filing from becoming a forecast.
        var observedGrowth = (f.Revenue / f.PreviousRevenue - 1m);
        if (!string.IsNullOrWhiteSpace(f.HistoricalRevenueJson))
        {
            try
            {
                var history = JsonSerializer.Deserialize<List<RevenueObservation>>(f.HistoricalRevenueJson, HistoryJson) ?? [];
                var oldest = history.OrderBy(x => x.PeriodEnd).FirstOrDefault();
                if (oldest is { Revenue: > 0 } && f.Revenue > 0 && oldest.PeriodEnd < f.PeriodEnd)
                    observedGrowth = (decimal)Math.Pow((double)(f.Revenue / oldest.Revenue), 1d / Math.Max(1, f.PeriodEnd.Year - oldest.PeriodEnd.Year)) - 1m;
            }
            catch (JsonException) { }
        }
        if (!string.IsNullOrWhiteSpace(f.HistoricalFinancialsJson))
        {
            try
            {
                var history = JsonSerializer.Deserialize<List<FinancialObservation>>(f.HistoricalFinancialsJson, HistoryJson) ?? [];
                var valid = history.Where(x => x.Fcf is > 0).OrderBy(x => x.PeriodEnd).ToArray();
                if (valid.Length >= 2)
                {
                    var oldest = valid[0];
                    var fcfGrowth = (decimal)Math.Pow((double)(f.OperatingCashFlow - f.CapitalExpenditure) / (double)Math.Max(.01m, oldest.Fcf!.Value), 1d / Math.Max(1, f.PeriodEnd.Year - oldest.PeriodEnd.Year)) - 1m;
                    observedGrowth = (observedGrowth + fcfGrowth) / 2m;
                }
            }
            catch (JsonException) { }
        }
        var growth = Math.Clamp(observedGrowth * .5m, -0.05m, 0.10m);
        var wacc = WaccCalculator.Calculate(treasuryYield);
        var riskFree = wacc.RiskFreeRate;
        var rate = WaccCalculator.Rate(wacc);
        decimal Dcf(decimal g, decimal rate, decimal terminal)
        {
            var annual = cash / f.Shares;
            decimal present = 0;
            for (var year = 1; year <= 5; year++)
            {
                annual *= 1 + g + (terminal - g) * (year - 1) / 8m;
                present += annual / (decimal)Math.Pow((double)(1 + rate), year);
            }
            return present + annual * (1 + terminal) / (rate - terminal) / (decimal)Math.Pow((double)(1 + rate), 5);
        }
        var dcfLow = Dcf(Math.Clamp(growth - .03m, -.08m, .08m), Math.Min(.20m, rate + .02m), .01m);
        var dcfBase = Dcf(growth, rate, .02m);
        var dcfHigh = Dcf(Math.Clamp(growth + .03m, -.02m, .13m), Math.Max(.09m, rate - .01m), .025m);
        // Convert enterprise value to equity value when SEC supplies the bridge.
        var equityAdjustment = (f.CashAndEquivalents ?? 0m) - (f.TotalDebt ?? 0m);
        dcfLow += equityAdjustment / f.Shares;
        dcfBase += equityAdjustment / f.Shares;
        dcfHigh += equityAdjustment / f.Shares;
        var relative = RelativeValuationCalculator.FairValue(f, sector, historicalPe);
        var fcfValue = FcfValuationCalculator.FairValue(f)!.Value;
        var evEbitda = EnterpriseValuationCalculator.FairValue(f, sector);
        var forwardPe = ForwardEarningsValuationCalculator.FairValue(f, sector);
        var pe = sector switch { "Technology" or "Communication Services" => 22m, "Healthcare" => 18m, "Consumer Defensive" => 20m, "Energy" => 12m, _ => 16m };
        var values = new[] { (Name: "DCF", Value: dcfBase, Weight: .45m), (Name: "P/E", Value: relative, Weight: .20m), (Name: "Forward P/E", Value: forwardPe, Weight: .10m), (Name: "EV/EBITDA", Value: evEbitda, Weight: .15m), (Name: "FCF", Value: fcfValue, Weight: .10m) }.Where(x => x.Value is > 0).ToArray();
        if (values.Length == 0) return null;
        var weightSum = values.Sum(x => x.Weight);
        var composite = values.Sum(x => x.Value!.Value * x.Weight) / weightSum;
        var low = Math.Min(dcfLow, composite * .75m);
        var high = Math.Max(dcfHigh, composite * 1.25m);
        var confidence = Math.Clamp(45m + values.Length * 15m + (f.PreviousRevenue > 0 ? 10m : 0m) - (f.NetIncome < 0 ? 25m : 0m), 0m, 100m);
        var basis = composite;
        var margin = Math.Clamp(20 + (high - low) / basis * 10, 25, 40);
        // A valuation has no market opinion until it is compared with the current price.
        // Keep a neutral cached score here; Evaluate reprices it using the latest close.
        var score = 50m * (confidence / 100m);
        var status = "N/A";
        return new(low, basis, high, growth * 100, rate * 100, 2)
        {
            Model = "DCF-5-composite", MarginOfSafety = margin,
            BuyBelow = Math.Min(low, basis * (1 - margin / 100))
            ,DcfFairValue = dcfBase, RelativeFairValue = relative, FcfFairValue = fcfValue, EvEbitdaFairValue = evEbitda, ForwardPeFairValue = forwardPe,
            CompositeFairValue = composite, RelativeMultiple = historicalPe is > 3 and < 60 ? historicalPe : pe, ValuationScore = Math.Round(score, 1), Confidence = confidence,
            Status = status, RiskFreeRate = riskFree, EquityRiskPremium = wacc.EquityRiskPremium, Beta = wacc.Beta,
            CostOfDebt = wacc.CostOfDebt, TaxRate = wacc.TaxRate, WaccFallbackUsed = wacc.UsedFallback,
            Cash = f.CashAndEquivalents, Debt = f.TotalDebt, DilutedShares = f.Shares,
            DataQuality = values.Length == 3 ? "Kompletní kontrola třemi metodami" : "Částečné metody; váhy byly přerozděleny",
            Weights = string.Join(", ", values.Select(x => $"{x.Name} {x.Weight / weightSum:P0}")),
            Explanation = $"DCF: 5 let normalizovaného FCF, růst {growth:P1}, WACC {rate:P1}. P/E {pe:0.0}× a dostupné forward P/E či EV/EBITDA násobky tvoří relativní kontrolu. FCF kontrola: FCF na akcii / požadovaný výnos 6 %."
        };
    }

    public static ValuationResult? Reprice(ValuationResult? valuation, decimal? currentPrice)
    {
        if (valuation is null || currentPrice is not > 0 || valuation.Base <= 0) return valuation;
        var upside = (valuation.Base / currentPrice.Value - 1m) * 100m;
        var score = Math.Clamp(50m + upside * 1.2m, 0m, 100m) * (valuation.Confidence / 100m);
        var status = upside >= 35 ? "Strongly Undervalued" : upside >= 15 ? "Undervalued" : upside > -10 ? "Fairly Valued" : upside > -30 ? "Overvalued" : "Strongly Overvalued";
        return valuation with { ValuationScore = Math.Round(score, 1), Status = status };
    }

    private sealed record RevenueObservation(DateOnly PeriodEnd, DateOnly Filed, decimal Revenue);
    private sealed record FinancialObservation(DateOnly PeriodEnd, DateOnly Filed, decimal Revenue, decimal? Fcf, decimal? NetIncome);

    public static ScreeningRow Evaluate(InvestmentSymbol stock, IReadOnlyList<MarketPrice> prices,
        FundamentalInput? fundamentals, IReadOnlyList<MacroObservation> macro, ScreeningResearch research, DateOnly today,
        ValuationResult? persistedValuation = null, bool allowLegacyCalculation = true)
    {
        var valid = prices.Where(p => p.Date <= today && p.Close > 0).DistinctBy(p => p.Date).OrderBy(p => p.Date).ToArray();
        var latest = valid.LastOrDefault();
        var fresh = latest != null && today.DayNumber - latest.Date.DayNumber <= 7;
        var row = new ScreeningRow { Symbol = stock.Symbol, Name = stock.Name, Sector = stock.Sector, Price = latest?.Close, PriceDate = latest?.Date, Evidence = research.Evidence };
        if (!fresh) row.Risks.Add("Cena chybí nebo je starší než 7 dní; ocenění a technické skóre nejsou aktivní.");
        void Add(string key, string label, int weight, decimal? score, string why) => row.Pillars.Add(new(key, label, weight, score is null ? null : Math.Clamp(score.Value, 0, 100), why));
        var f = research.Fundamentals ?? fundamentals;
        var fValid = f != null && f.PeriodEnd <= today && f.Filed <= today && f.Filed >= f.PeriodEnd && today.DayNumber - f.PeriodEnd.DayNumber <= 460 && f.Revenue > 0 && f.Assets > 0 && f.Liabilities >= 0;
        row.Fundamentals = f;
        if (f != null && !fValid) row.Risks.Add("Výkazy jsou zastaralé nebo neúplné; kvalita a ocenění se nezapočítávají.");
        decimal? quality = null;
        if (fValid)
        {
            var revenueGrowth = f!.PreviousRevenue > 0 ? (f.Revenue / f.PreviousRevenue - 1) * 100 : (decimal?)null;
            if (revenueGrowth != null) quality = Math.Clamp(50 + revenueGrowth.Value * 2, 0, 100) * .3m
                + Math.Clamp(f.NetIncome / f.Revenue * 400, 0, 100) * .25m
                + Math.Clamp((f.OperatingCashFlow - f.CapitalExpenditure) / f.Revenue * 500, 0, 100) * .25m
                + Math.Clamp((1 - f.Liabilities / f.Assets) * 100, 0, 100) * .2m;
            if (f.NetIncome < 0 || f.OperatingCashFlow <= 0) row.Risks.Add("Ztráta nebo nekladný provozní cash flow: nutná individuální revize.");
            if (f.Liabilities > f.Assets) row.Risks.Add("Záporný vlastní kapitál: prověřit rozvahu a buybacky.");
            row.Valuation = Reprice(persistedValuation ?? (allowLegacyCalculation ? Value(f, stock.Sector, macro.FirstOrDefault(m => m.Series == "DGS10" && today.DayNumber - m.Date.DayNumber is >= 0 and <= 10)?.Value) : null), latest?.Close);
            row.ValuationScore = row.Valuation?.ValuationScore;
            row.ValuationConfidence = row.Valuation?.Confidence;
            row.ValuationStatus = row.Valuation?.Status ?? "N/A";
        }
        // The decision score contains only company quality, valuation and entry timing.
        // Macro and geopolitics remain context and valuation inputs, not false precision.
        Add("quality", "Kvalita", 35, quality, "Roční růst tržeb 30 %, čistá marže 25 %, FCF marže 25 %, podíl vlastního kapitálu 20 %. Heuristika, nikoli sektorově kalibrovaný rating.");
        if (row.Valuation != null && fresh && row.Price > 0)
            row.Discount = (1 - row.Price.Value / row.Valuation.Base) * 100;
        Add("valuation", "Ocenění", 45, row.Discount is {} discount ? 50 + discount : null,
            "DCF CFO − capex na akcii: 5 let s útlumem růstu, tři scénáře. Diskont CAPM/WACC (min. 9 %), při chybějících vstupech konzervativní fallback. Odhad se porovnává s aktuální cenou až při načtení snapshotu. Banky a REIT vyžadují jiný model.");
        if (row.Valuation is null) row.Risks.Add("Férová hodnota není dostupná: neúplné výkazy, nekladný FCF nebo nepodporovaný sektor.");
        var rates = macro.FirstOrDefault(m => m.Series == "DGS10" && today.DayNumber - m.Date.DayNumber is >= 0 and <= 10);
        var unemployment = macro.FirstOrDefault(m => m.Series == "UNRATE" && today.DayNumber - m.Date.DayNumber is >= 0 and <= 65);
        decimal? macroScore = null;
        if (rates?.PriorValue != null && unemployment?.PriorValue != null)
        {
            var sensitive = stock.Sector is "Technology" or "Real Estate" or "Utilities" or "Consumer Cyclical" ? 1.5m : 1m;
            macroScore = 50 - (rates.Value - rates.PriorValue.Value) * 15 * sensitive - (unemployment.Value - unemployment.PriorValue.Value) * 25;
        }
        var macroEvidence = EvidenceScore(research.Evidence, "macro", today);
        Add("macro", "Makro kontext", 0, macroEvidence ?? macroScore, "Makro se používá jako kontext a pro diskontní sazbu ocenění. Samostatné makro skóre se do investičního rozhodnutí nezapočítává.");
        decimal? technical = null;
        if (valid.Length >= 252 && fresh)
        {
            var ema = UnderEma200Calculator.CalculateEma(valid.Select(p => p.Close).ToArray(), 200);
            row.EmaDistance = (latest!.Close / ema[^1] - 1) * 100;
            var momentum = (latest.Close / valid[^22].Close - 1) * 100;
            technical = 50 + Math.Clamp(momentum * 2, -25, 25) + Math.Clamp(row.EmaDistance.Value, -25, 25);
        }
        Add("technical", "Načasování", 20, technical, "Stabilizace: 21denní momentum a trend vůči denní EMA200. Samotná hloubka propadu není bonus za levnou akcii.");
        var geopolitical = macro.FirstOrDefault(m => m.Series == "GEO_RISK" && today.DayNumber - m.Date.DayNumber is >= 0 and <= 3);
        Add("geopolitics", "Geopolitický kontext", 0, EvidenceScore(research.Evidence, "geopolitics", today) ?? geopolitical?.Value,
            geopolitical is null ? "Chybí aktuální agregovaný geopolitický kontext." : "Široký index z veřejných geopolitických titulků GDELT; vyšší skóre znamená nižší zachycené riziko. Neurčuje expozici konkrétní firmy.");
        Add("analysts", "Analytický kontext", 0, EvidenceScore(research.Evidence, "analysts", today), "Pouze rozbory s autorem a označenou prověřenou historií; deklarace uživatele není nezávislý audit autora. Duplicitní URL se počítá jednou.");
        foreach (var key in new[] { "macro", "geopolitics", "analysts" })
            if (research.Evidence.Any(e => e.Pillar == key) && EvidenceScore(research.Evidence, key, today) == null)
                row.Risks.Add($"Rozbory ({key}) nemají platný způsobilý záznam: ověř datum, horizont a historii autora.");
        row.Coverage = row.Pillars.Where(p => p.Score != null).Sum(p => p.Weight);
        // Never renormalize sparse observations into an apparently complete investment score.
        if (quality != null && row.Discount != null && row.Coverage >= 80)
            row.Score = Math.Round(row.Pillars.Sum(p => (p.Score ?? 0) * p.Weight) / 100, 1);
        row.Verdict = row.Score is null ? "Neúplné hodnocení" : row.Risks.Any(r => r.StartsWith("Ztráta") || r.StartsWith("Záporný")) ? "Nutná revize rizik" : row.Score >= 70 ? "K hlubší analýze" : row.Score >= 50 ? "Sledovat" : "Slabší souběh faktorů";
        row.Entry = EntryCalculator.Evaluate(row, valid, today);
        return row;
    }

    public static decimal? EvidenceScore(IEnumerable<ResearchEvidence> evidence, string pillar, DateOnly today)
    {
        var available = evidence.Where(e => e.Pillar == pillar && e.Published <= today && e.HorizonDays is >= 1 and <= 365
            && today.DayNumber - e.Published.DayNumber <= Math.Min(e.HorizonDays, pillar == "analysts" ? 180 : 90)
            && e.Score is >= 0 and <= 100 && Uri.TryCreate(e.SourceUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https"
            && !string.IsNullOrWhiteSpace(e.Thesis) && !string.IsNullOrWhiteSpace(e.Risk)
            && (pillar != "analysts" || e.TrackRecordVerified && !string.IsNullOrWhiteSpace(e.Author)))
            .DistinctBy(e => e.SourceUrl.TrimEnd('/'), StringComparer.OrdinalIgnoreCase).ToArray();
        return available.Length == 0 ? null : available.Average(e => e.Score);
    }
}
