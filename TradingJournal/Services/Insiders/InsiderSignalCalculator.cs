using TradingJournal.Models;

namespace TradingJournal.Services.Insiders;

public sealed record InsiderSignal(decimal? Score, int Coverage, string Explanation);

public static class InsiderSignalCalculator
{
    public static InsiderSignal Calculate(IReadOnlyCollection<InsiderReportingOwner> owners, decimal value,
        decimal? ownershipIncreasePercent, int clusterInsiders, int repeatPurchaseDays)
    {
        var role = owners.Count == 0 ? (decimal?)null : owners.Max(RoleScore);
        var amount = value >= 1_000_000 ? 100m : value >= 250_000 ? 85m : value >= 100_000 ? 70m : value >= 25_000 ? 55m : 35m;
        var ownership = ownershipIncreasePercent switch { >= 25 => 100m, >= 10 => 85m, >= 5 => 70m, >= 1 => 55m, not null => 40m, _ => (decimal?)null };
        var cluster = clusterInsiders >= 3 ? 100m : clusterInsiders == 2 ? 75m : 40m;
        var repeat = repeatPurchaseDays >= 3 ? 100m : repeatPurchaseDays == 2 ? 70m : 40m;
        var weighted = (role ?? 0) * .25m + amount * .30m + (ownership ?? 0) * .20m + cluster * .15m + repeat * .10m;
        var coverage = (role == null ? 0 : 25) + 30 + (ownership == null ? 0 : 20) + 15 + 10;
        decimal? score = coverage >= 70 ? decimal.Round(weighted, 0) : null;
        return new(score, coverage,
            $"role {Format(role)} · částka {amount:0} · změna podílu {Format(ownership)} · cluster {cluster:0} · opakování {repeat:0}");
    }

    public static decimal RoleScore(InsiderReportingOwner owner)
    {
        var title = owner.OfficerTitle?.ToUpperInvariant() ?? "";
        if (title.Contains("CEO") || title.Contains("CHIEF EXECUTIVE")) return 100;
        if (title.Contains("CFO") || title.Contains("CHIEF FINANCIAL")) return 95;
        if (title.Contains("PRESIDENT") || title.Contains("COO") || title.Contains("CHAIR")) return 85;
        if (owner.IsOfficer) return 75;
        if (owner.IsDirector) return 65;
        if (owner.IsTenPercentOwner) return 45;
        return 35;
    }

    public static string RoleLabel(IEnumerable<InsiderReportingOwner> owners)
    {
        var labels = owners.SelectMany(o => new[] { o.OfficerTitle, o.IsOfficer && string.IsNullOrWhiteSpace(o.OfficerTitle) ? "Officer" : null, o.IsDirector ? "Director" : null,
            o.IsTenPercentOwner ? "10% vlastník" : null, o.IsOther ? "Jiná role" : null })
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(" · ", labels.DefaultIfEmpty("Neuvedeno"));
    }

    private static string Format(decimal? value) => value?.ToString("0") ?? "—";
}
