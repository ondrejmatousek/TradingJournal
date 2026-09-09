using TradingJournal.Models;

namespace TradingJournal.Services.Screening;

public static class ScreeningCalculator
{
    public static decimal Median(IEnumerable<decimal> values)
    {
        var a = values.Order().ToArray();
        return a.Length % 2 == 0 ? (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2 : a[a.Length / 2];
    }

    public static SeasonalWindow Seasonality(IReadOnlyList<SeasonalityPrice> prices, DateOnly asOf, int days)
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

    public static ValuationResult? Value(FundamentalInput f, string sector, decimal? treasuryYield = null)
    {
        // Cash from operations less capex is an equity cash-flow proxy, not FCFF: no second debt subtraction.
        if (sector is "Financial Services" or "Real Estate" || f.Currency != "USD" || f.Shares <= 0 || f.PreviousRevenue <= 0 || f.Revenue <= 0) return null;
        var cash = f.OperatingCashFlow - f.CapitalExpenditure;
        if (cash <= 0 || f.CapitalExpenditure < 0) return null;
        // One annual report is not a normalized earnings history: shrink observed growth
        // and retain a conservative scenario instead of extrapolating a single strong year.
        var growth = Math.Clamp((f.Revenue / f.PreviousRevenue - 1) * .5m, -0.05m, 0.10m);
        var rate = treasuryYield is >= 0 and <= 15 ? Math.Clamp(treasuryYield.Value / 100 + .06m, .09m, .20m) : .11m;
        decimal Dcf(decimal g, decimal rate, decimal terminal)
        {
            var annual = cash / f.Shares;
            decimal present = 0;
            for (var year = 1; year <= 10; year++)
            {
                var fadedGrowth = year <= 5 ? g : g + (terminal - g) * (year - 5) / 5;
                annual *= 1 + fadedGrowth;
                present += annual / (decimal)Math.Pow((double)(1 + rate), year);
            }
            return present + annual * (1 + terminal) / (rate - terminal) / (decimal)Math.Pow((double)(1 + rate), 10);
        }
        var low = Dcf(growth - .03m, rate + .02m, .01m) * .8m;
        var basis = Dcf(growth, rate, .02m);
        var high = Dcf(growth + .03m, rate - .01m, .025m) * 1.1m;
        var margin = Math.Clamp(20 + (high - low) / basis * 10, 25, 40);
        return new(low, basis, high, growth * 100, rate * 100, 2)
        {
            Model = "DCF-10-v2", MarginOfSafety = margin,
            BuyBelow = Math.Min(low, basis * (1 - margin / 100))
        };
    }

    public static ScreeningRow Evaluate(InvestmentSymbol stock, IReadOnlyList<SeasonalityPrice> prices,
        FundamentalInput? fundamentals, IReadOnlyList<MacroObservation> macro, ScreeningResearch research, DateOnly today)
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
            row.Valuation = Value(f, stock.Sector, macro.FirstOrDefault(m => m.Series == "DGS10" && today.DayNumber - m.Date.DayNumber is >= 0 and <= 10)?.Value);
        }
        Add("quality", "Kvalita", 25, quality, "Roční růst tržeb 30 %, čistá marže 25 %, FCF marže 25 %, podíl vlastního kapitálu 20 %. Heuristika, nikoli sektorově kalibrovaný rating.");
        if (row.Valuation != null && fresh && row.Price > 0)
            row.Discount = (1 - row.Price.Value / row.Valuation.Base) * 100;
        Add("valuation", "Ocenění", 30, row.Discount is {} discount ? 50 + discount : null,
            "DCF CFO − capex na akcii: 10 let, útlum růstu v letech 6–10, tři scénáře. Diskont US10Y + 6 p. b. (min. 9 %), bez aktuálních sazeb 11 %. Jednoletý cash flow není víceletý normalizovaný zisk. Banky a REIT vyžadují jiný model.");
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
        Add("macro", "Makro", 15, macroEvidence ?? macroScore, "Změna amerického 10letého výnosu a nezaměstnanosti proti cca 3 měsícům; vyšší citlivost růstových a úrokově citlivých sektorů. Datovaný vlastní rozbor má přednost. Nejde o úplný makromodel.");
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
        Add("geopolitics", "Geopolitika", 5, EvidenceScore(research.Evidence, "geopolitics", today) ?? geopolitical?.Value,
            geopolitical is null ? "Chybí aktuální agregovaný geopolitický kontext." : "Široký index z veřejných geopolitických titulků GDELT; vyšší skóre znamená nižší zachycené riziko. Neurčuje expozici konkrétní firmy.");
        Add("analysts", "Analýzy", 5, EvidenceScore(research.Evidence, "analysts", today), "Pouze rozbory s autorem a označenou prověřenou historií; deklarace uživatele není nezávislý audit autora. Duplicitní URL se počítá jednou.");
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
