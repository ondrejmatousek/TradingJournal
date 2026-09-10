namespace TradingJournal.Services.Valuation;

/// <summary>Eligibility routing is independent of the ticker or an expected target price.</summary>
public interface IValuationStrategy
{
    string Name { get; }
    bool Matches(string sector);
    string? UnsupportedReason { get; }
}

public sealed class FinancialInstitutionStrategy : IValuationStrategy
{
    public string Name => "FinancialInstitution";
    public bool Matches(string sector) => new[] { "Financial Services", "Financials", "Bank", "Banks", "Insurance" }.Contains(sector.Trim(), StringComparer.OrdinalIgnoreCase);
    public string? UnsupportedReason => "Bank/insurance/equity strategy requires verified company classification and regulatory capital data. Industrial FCFF is not substituted.";
}

public sealed class ReitStrategy : IValuationStrategy
{
    public string Name => "REIT";
    public bool Matches(string sector) => new[] { "Real Estate", "REIT", "REITs" }.Contains(sector.Trim(), StringComparer.OrdinalIgnoreCase);
    public string? UnsupportedReason => "REIT requires verified AFFO/NAV and property liabilities. Industrial FCFF is not substituted.";
}

public sealed class StandardNonFinancialStrategy : IValuationStrategy
{
    public string Name => "StandardNonFinancial";
    public bool Matches(string sector) => new[] { "Technology", "Semiconductors", "Communication Services", "Consumer Cyclical", "Consumer Defensive", "Healthcare", "Industrials", "Energy", "Basic Materials", "Utilities" }.Contains(sector.Trim(), StringComparer.OrdinalIgnoreCase);
    public string? UnsupportedReason => null;
}
