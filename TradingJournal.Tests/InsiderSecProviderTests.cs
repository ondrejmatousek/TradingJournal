using TradingJournal.Services.Insiders;

namespace TradingJournal.Tests;

public sealed class InsiderSecProviderTests
{
    [Fact]
    public void ParseSubmissions_KeepsOnlyRecentOwnershipForms()
    {
        const string json = """{"name":"Example Inc","filings":{"recent":{"accessionNumber":["1","2","3"],"filingDate":["2026-09-01","2026-09-02","2025-01-01"],"reportDate":["2026-08-31","2026-09-01","2024-12-31"],"form":["4","10-K","4/A"],"primaryDocument":["a.xml","b.htm","c.xml"]}}}""";
        var result = InsiderSecProvider.ParseSubmissions(json, 123, new DateOnly(2026, 8, 1));
        var filing = Assert.Single(result);
        Assert.Equal("1", filing.AccessionNumber);
        Assert.Equal("4", filing.FormType);
    }

    [Fact]
    public void ParseOwnershipXml_SeparatesPurchaseFromAwardAndReadsRole()
    {
        const string xml = """<ownershipDocument><periodOfReport>2026-09-01</periodOfReport><issuer><issuerCik>123</issuerCik><issuerName>Example Inc</issuerName><issuerTradingSymbol>EXM</issuerTradingSymbol></issuer><reportingOwner><reportingOwnerId><rptOwnerCik>456</rptOwnerCik><rptOwnerName>DOE JANE</rptOwnerName></reportingOwnerId><reportingOwnerRelationship><isDirector>0</isDirector><isOfficer>1</isOfficer><officerTitle>Chief Executive Officer</officerTitle></reportingOwnerRelationship></reportingOwner><nonDerivativeTable><nonDerivativeTransaction><securityTitle><value>Common Stock</value></securityTitle><transactionDate><value>2026-09-01</value></transactionDate><transactionCoding><transactionCode>P</transactionCode><aff10b5One>0</aff10b5One></transactionCoding><transactionAmounts><transactionShares><value>1000</value></transactionShares><transactionPricePerShare><value>25.50</value></transactionPricePerShare><transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode></transactionAmounts><postTransactionAmounts><sharesOwnedFollowingTransaction><value>5000</value></sharesOwnedFollowingTransaction></postTransactionAmounts><ownershipNature><directOrIndirectOwnership><value>D</value></directOrIndirectOwnership></ownershipNature></nonDerivativeTransaction><nonDerivativeTransaction><securityTitle><value>Common Stock</value></securityTitle><transactionDate><value>2026-09-01</value></transactionDate><transactionCoding><transactionCode>A</transactionCode></transactionCoding><transactionAmounts><transactionShares><value>20</value></transactionShares><transactionAcquiredDisposedCode><value>A</value></transactionAcquiredDisposedCode></transactionAmounts></nonDerivativeTransaction></nonDerivativeTable></ownershipDocument>""";
        var result = InsiderSecProvider.ParseOwnershipXml(xml, "EXM", "0001", new DateOnly(2026, 9, 2), null, "4", "https://sec.test/filing.xml");
        Assert.Equal("EXM", result.Symbol);
        Assert.Equal("Chief Executive Officer", Assert.Single(result.ReportingOwners).OfficerTitle);
        Assert.Equal(2, result.Transactions.Count);
        var purchase = result.Transactions.Single(x => x.TransactionCode == "P");
        Assert.Equal(25_500m, purchase.Shares * purchase.PricePerShare);
        Assert.False(purchase.IsTenB5One);
    }
}
