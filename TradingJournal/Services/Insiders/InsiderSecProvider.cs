using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using TradingJournal.Models;
using TradingJournal.Services.Screening;

namespace TradingJournal.Services.Insiders;

public sealed record SecOwnershipFiling(string AccessionNumber, DateOnly FiledDate, DateOnly? PeriodOfReport,
    string FormType, string PrimaryDocument, string CompanyName, long Cik);

public interface IInsiderSecProvider
{
    Task<IReadOnlyList<SecOwnershipFiling>> RecentFilingsAsync(InvestmentSymbol stock, DateOnly since, CancellationToken ct);
    Task<InsiderFiling> FilingAsync(InvestmentSymbol stock, SecOwnershipFiling item, CancellationToken ct);
}

public sealed class InsiderSecProvider(IHttpClientFactory factory, IConfiguration configuration, IWebHostEnvironment environment) : IInsiderSecProvider
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private Dictionary<string, long>? cikBySymbol;

    public async Task<IReadOnlyList<SecOwnershipFiling>> RecentFilingsAsync(InvestmentSymbol stock, DateOnly since, CancellationToken ct)
    {
        var cik = await CikAsync(stock.Symbol, ct);
        if (cik == null) return [];
        var url = $"https://data.sec.gov/submissions/CIK{cik:0000000000}.json";
        var json = await GetStringAsync(url, ct);
        return ParseSubmissions(json, cik.Value, since);
    }

    public async Task<InsiderFiling> FilingAsync(InvestmentSymbol stock, SecOwnershipFiling item, CancellationToken ct)
    {
        var accessionPath = item.AccessionNumber.Replace("-", "", StringComparison.Ordinal);
        var url = $"https://www.sec.gov/Archives/edgar/data/{item.Cik}/{accessionPath}/{item.PrimaryDocument}";
        var filing = ParseOwnershipXml(await GetStringAsync(url, ct), stock.Symbol, item.AccessionNumber,
            item.FiledDate, item.PeriodOfReport, item.FormType, url);
        filing.IssuerName = string.IsNullOrWhiteSpace(filing.IssuerName) ? item.CompanyName : filing.IssuerName;
        filing.IssuerCik = item.Cik.ToString(CultureInfo.InvariantCulture);
        filing.ImportedAt = DateTimeOffset.UtcNow;
        return filing;
    }

    private async Task<long?> CikAsync(string symbol, CancellationToken ct)
    {
        if (cikBySymbol == null)
        {
            var path = Path.Combine(environment.ContentRootPath, "Docs", "investments", "sec-tickers.json");
            cikBySymbol = JsonSerializer.Deserialize<Dictionary<string, long>>(await File.ReadAllTextAsync(path, ct))
                ?? throw new InvalidDataException("SEC ticker mapping is empty.");
        }
        return cikBySymbol.GetValueOrDefault(symbol);
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            using var client = factory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                configuration["Screening:SecUserAgent"] ?? "EdgeDesk/1.0 investment-research contact@example.com");
            var result = await client.GetStringAsync(url, ct);
            // Stay well below SEC's published maximum of ten requests per second.
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            return result;
        }
        finally { Gate.Release(); }
    }

    public static IReadOnlyList<SecOwnershipFiling> ParseSubmissions(string json, long cik, DateOnly since)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var companyName = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
        if (!root.TryGetProperty("filings", out var filings) || !filings.TryGetProperty("recent", out var recent)) return [];
        var accessions = recent.GetProperty("accessionNumber");
        var dates = recent.GetProperty("filingDate");
        var reports = recent.GetProperty("reportDate");
        var forms = recent.GetProperty("form");
        var documents = recent.GetProperty("primaryDocument");
        var result = new List<SecOwnershipFiling>();
        for (var i = 0; i < accessions.GetArrayLength(); i++)
        {
            var form = forms[i].GetString() ?? "";
            if (form is not ("4" or "4/A" or "5" or "5/A")) continue;
            if (!DateOnly.TryParseExact(dates[i].GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var filed) || filed < since) continue;
            DateOnly? report = DateOnly.TryParseExact(reports[i].GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedReport) ? parsedReport : null;
            result.Add(new(accessions[i].GetString()!, filed, report, form, documents[i].GetString()!, companyName, cik));
        }
        return result;
    }

    public static InsiderFiling ParseOwnershipXml(string xml, string fallbackSymbol, string accessionNumber,
        DateOnly filedDate, DateOnly? periodOfReport, string formType, string sourceUrl)
    {
        var document = XDocument.Parse(xml, LoadOptions.None);
        static IEnumerable<XElement> Children(XContainer node, string name) => node.Elements().Where(x => x.Name.LocalName == name);
        static XElement? Child(XContainer node, string name) => Children(node, name).FirstOrDefault();
        static string? Value(XContainer? node, params string[] path)
        {
            foreach (var part in path)
            {
                if (node == null) return null;
                node = Child(node, part);
            }
            return (node as XElement)?.Value.Trim();
        }
        static bool Flag(string? value) => value is "1" or "true" or "True";
        static decimal? Number(string? value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? number : null;
        static DateOnly? Date(string? value) => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

        var root = document.Root ?? throw new InvalidDataException("Ownership filing has no root element.");
        var issuer = Child(root, "issuer");
        var filing = new InsiderFiling
        {
            AccessionNumber = accessionNumber,
            Symbol = Value(issuer, "issuerTradingSymbol") ?? fallbackSymbol,
            IssuerName = Value(issuer, "issuerName") ?? "",
            FormType = formType,
            FiledDate = filedDate,
            PeriodOfReport = Date(Value(root, "periodOfReport")) ?? periodOfReport,
            SourceUrl = sourceUrl,
            IsAmendment = formType.EndsWith("/A", StringComparison.Ordinal)
        };
        foreach (var owner in Children(root, "reportingOwner"))
        {
            var identity = Child(owner, "reportingOwnerId");
            var relation = Child(owner, "reportingOwnerRelationship");
            filing.ReportingOwners.Add(new InsiderReportingOwner
            {
                OwnerCik = Value(identity, "rptOwnerCik"), Name = Value(identity, "rptOwnerName") ?? "Neznámý insider",
                IsDirector = Flag(Value(relation, "isDirector")), IsOfficer = Flag(Value(relation, "isOfficer")),
                IsTenPercentOwner = Flag(Value(relation, "isTenPercentOwner")), IsOther = Flag(Value(relation, "isOther")),
                OfficerTitle = Value(relation, "officerTitle")
            });
        }
        var index = 0;
        void AddTransactions(string tableName, string rowName, bool derivative)
        {
            var table = Child(root, tableName);
            if (table == null) return;
            foreach (var row in Children(table, rowName))
            {
                var amounts = Child(row, "transactionAmounts");
                var post = Child(row, "postTransactionAmounts");
                var ownership = Child(row, "ownershipNature");
                var coding = Child(row, "transactionCoding");
                var date = Date(Value(row, "transactionDate", "value"));
                var shares = Number(Value(amounts, "transactionShares", "value"));
                if (date == null || shares == null) continue;
                filing.Transactions.Add(new InsiderTransaction
                {
                    SourceIndex = index++, TransactionDate = date.Value,
                    SecurityTitle = Value(row, "securityTitle", "value") ?? "Cenný papír",
                    TransactionCode = Value(coding, "transactionCode") ?? "",
                    AcquisitionDisposition = Value(amounts, "transactionAcquiredDisposedCode", "value") ?? "",
                    Shares = shares.Value, PricePerShare = Number(Value(amounts, "transactionPricePerShare", "value")),
                    SharesOwnedAfter = Number(Value(post, "sharesOwnedFollowingTransaction", "value")),
                    DirectIndirect = Value(ownership, "directOrIndirectOwnership", "value"), IsDerivative = derivative,
                    IsTenB5One = Value(coding, "aff10b5One") is { } plan ? Flag(plan) : null
                });
            }
        }
        AddTransactions("nonDerivativeTable", "nonDerivativeTransaction", false);
        AddTransactions("derivativeTable", "derivativeTransaction", true);
        return filing;
    }
}
