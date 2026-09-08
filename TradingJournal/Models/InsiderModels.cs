namespace TradingJournal.Models;

public sealed class InsiderFiling
{
    public long Id { get; set; }
    public string AccessionNumber { get; set; } = "";
    public string IssuerCik { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string IssuerName { get; set; } = "";
    public string FormType { get; set; } = "";
    public DateOnly? PeriodOfReport { get; set; }
    public DateOnly FiledDate { get; set; }
    public string SourceUrl { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
    public bool IsAmendment { get; set; }
    public List<InsiderReportingOwner> ReportingOwners { get; set; } = [];
    public List<InsiderTransaction> Transactions { get; set; } = [];
}

public sealed class InsiderReportingOwner
{
    public long Id { get; set; }
    public long InsiderFilingId { get; set; }
    public InsiderFiling Filing { get; set; } = null!;
    public string? OwnerCik { get; set; }
    public string Name { get; set; } = "";
    public bool IsDirector { get; set; }
    public bool IsOfficer { get; set; }
    public bool IsTenPercentOwner { get; set; }
    public bool IsOther { get; set; }
    public string? OfficerTitle { get; set; }
}

public sealed class InsiderTransaction
{
    public long Id { get; set; }
    public long InsiderFilingId { get; set; }
    public InsiderFiling Filing { get; set; } = null!;
    public int SourceIndex { get; set; }
    public DateOnly TransactionDate { get; set; }
    public string SecurityTitle { get; set; } = "";
    public string TransactionCode { get; set; } = "";
    public string AcquisitionDisposition { get; set; } = "";
    public decimal Shares { get; set; }
    public decimal? PricePerShare { get; set; }
    public decimal? SharesOwnedAfter { get; set; }
    public string? DirectIndirect { get; set; }
    public bool IsDerivative { get; set; }
    public bool? IsTenB5One { get; set; }
}

public sealed class InsiderImportRun
{
    public long Id { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int IssuersChecked { get; set; }
    public int FilingsImported { get; set; }
    public int TransactionsImported { get; set; }
    public int IssuersFailed { get; set; }
    public string? ErrorSummary { get; set; }
}
