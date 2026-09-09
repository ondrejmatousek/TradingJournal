using TradingJournal.Models;

namespace TradingJournal.Services.Screening;

public static class EntryCalculator
{
    public static EntryAssessment Evaluate(ScreeningRow row, IReadOnlyList<SeasonalityPrice> prices, DateOnly today)
    {
        var reasons = new List<string>();
        EntryAssessment Result(string status, string label, decimal? low = null, decimal? high = null,
            decimal? support = null, decimal? momentum = null) => new(status, label, low, high, support, momentum, reasons);
        var v = row.Valuation;
        var f = row.Fundamentals;
        if (row.Price is not > 0 || row.PriceDate is null || today.DayNumber - row.PriceDate.Value.DayNumber is < 0 or > 7
            || v is null || v.Model != "DCF-10-v2" || v.Base <= 0 || f is null || f.Revenue <= 0
            || f.PeriodEnd > today || f.Filed > today || f.Filed < f.PeriodEnd || today.DayNumber - f.PeriodEnd.DayNumber > 460)
        {
            reasons.Add("Chybí aktuální cena nebo použitelné ocenění; nákupní závěr nelze vytvořit.");
            return Result("insufficient", "Nedostatek dat");
        }
        reasons.Add(FormattableString.Invariant($"Cena {row.Price:0.00} USD, základní odhad {v.Base:0.00} USD; odchylka {(1 - row.Price.Value / v.Base) * 100:0.0} %."));
        reasons.Add(FormattableString.Invariant($"Požadovaná rezerva {v.MarginOfSafety:0.0} %. Cenový limit {v.BuyBelow:0.00} USD je nižší z konzervativního scénáře a základního odhadu po odečtení rezervy."));
        reasons.Add("Model vychází z jednoho ročního cash flow; budoucí financování, ředění a termín výsledků nejsou ověřeny. Nejde o živou kotaci ani kompletní rozbor rizik.");
        var quality = row.Pillars.FirstOrDefault(p => p.Key == "quality")?.Score;
        if (quality is null) return Result("insufficient", "Chybí hodnocení kvality");
        reasons.Add(FormattableString.Invariant($"Kvalita {quality:0}/100 (požadováno alespoň 60); čistá marže {f.NetIncome / f.Revenue * 100:0.0} %, FCF marže {(f.OperatingCashFlow - f.CapitalExpenditure) / f.Revenue * 100:0.0} %."));
        if (quality < 60 || f.NetIncome <= 0 || f.OperatingCashFlow <= f.CapitalExpenditure || f.Liabilities >= f.Assets)
        {
            reasons.Add("Firma nesplňuje kontrolu kvality, kladného zisku a FCF nebo kladného vlastního kapitálu. Sleva sama nestačí.");
            return Result("risk", "Nutná revize firmy");
        }
        if (row.Price > v.BuyBelow)
        {
            reasons.Add("Cena zatím nesplňuje požadovanou bezpečnostní rezervu.");
            return Result("expensive", row.Price < v.Base ? "Sleva nestačí" : "Cena nad odhadem");
        }
        var history = prices.Where(p => p.Date <= today && p.Close > 0).DistinctBy(p => p.Date).OrderBy(p => p.Date).TakeLast(252).ToArray();
        if (history.Length < 252 || history[^1].Date != row.PriceDate || history[^1].Close != row.Price
            || history.Zip(history.Skip(1)).Any(p => p.Second.Date.DayNumber - p.First.Date.DayNumber > 10))
        {
            reasons.Add("Ocenění je zajímavé, ale chybí 252 navazujících cen pro posouzení vstupu.");
            return Result("insufficient", "Levná; načasování neověřeno");
        }
        var closes = history.Select(p => p.Close).ToArray();
        var ema50 = UnderEma200Calculator.CalculateEma(closes, 50);
        var momentum = (closes[^1] / closes[^22] - 1) * 100;
        // Prior 60 closes exclude today's price. This is a closing-price support proxy,
        // not an OHLC/volume-confirmed support level.
        var support = closes.Take(closes.Length - 1).TakeLast(60).Order().ElementAt(5);
        var low = support * .97m;
        var high = Math.Min(support * 1.05m, v.BuyBelow);
        var stabilizing = momentum >= 0 && closes[^1] >= ema50[^1] && ema50[^1] >= ema50[^6];
        reasons.Add(FormattableString.Invariant($"21denní změna {momentum:0.0} %, cena {closes[^1]:0.00} USD vs. EMA50 {ema50[^1]:0.00} USD; EMA50 {(ema50[^1] >= ema50[^6] ? "neklesá" : "klesá")}."));
        reasons.Add(FormattableString.Invariant($"Orientační podpora {support:0.00} USD: spodní decil předchozích 60 závěrečných cen. Pásmo −3 % až +5 %, omezené cenovým limitem; bez ověření objemem a intradenními minimy."));
        if (low > high)
        {
            reasons.Add("Technické pásmo se nepřekrývá s přijatelnou cenou podle ocenění.");
            return Result("wait", "Levná; zóna nepotvrzena", support: support, momentum: momentum);
        }
        var inZone = row.Price >= low && row.Price <= high;
        reasons.Add(stabilizing && inZone
            ? "Cena je v průniku pásem a splňuje podmínky stabilizace. Kandidát k prověření nákupu."
            : "Čekat: cena není v pásmu nebo ještě nesplňuje všechny podmínky stabilizace.");
        return Result(stabilizing && inZone ? "candidate" : "wait", stabilizing && inZone ? "Kandidát k nákupu" : "Levná; čekat na potvrzení", low, high, support, momentum);
    }
}
