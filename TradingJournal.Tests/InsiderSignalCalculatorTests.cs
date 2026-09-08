using TradingJournal.Models;
using TradingJournal.Services.Insiders;

namespace TradingJournal.Tests;

public sealed class InsiderSignalCalculatorTests
{
    [Fact]
    public void CeoMillionDollarClusterPurchase_IsStrongAndFullyCovered()
    {
        var owners = new[] { new InsiderReportingOwner { Name = "Jane Doe", IsOfficer = true, OfficerTitle = "Chief Executive Officer" } };
        var result = InsiderSignalCalculator.Calculate(owners, 1_200_000m, 30m, 3, 3);
        Assert.Equal(100, result.Coverage);
        Assert.Equal(100m, result.Score);
    }

    [Fact]
    public void MissingOwnership_DoesNotReceiveItsWeight()
    {
        var owners = new[] { new InsiderReportingOwner { Name = "Director", IsDirector = true } };
        var result = InsiderSignalCalculator.Calculate(owners, 30_000m, null, 1, 1);
        Assert.Equal(80, result.Coverage);
        Assert.Equal(43m, result.Score);
    }

    [Theory]
    [InlineData("CEO", 100)]
    [InlineData("Chief Financial Officer", 95)]
    [InlineData("President", 85)]
    public void KeyOfficerTitles_HaveExplicitPriority(string title, decimal expected)
        => Assert.Equal(expected, InsiderSignalCalculator.RoleScore(new InsiderReportingOwner { IsOfficer = true, OfficerTitle = title }));
}
