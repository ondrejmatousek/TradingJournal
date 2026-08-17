using TradingJournal.Models;

namespace TradingJournal.ViewModels
{
    public class SetupReviewViewModel
    {
        public IReadOnlyList<TradeSetupReview> Reviews { get; init; } = [];

        public SetupReviewStats Stats { get; init; } = new();

        public TradeSetupReview Form { get; init; } = new();
    }

    public class SetupReviewStats
    {
        public int TotalSetups { get; init; }

        public int ReviewedSetups { get; init; }

        public int CompletedSetups { get; init; }

        public int Wins { get; init; }

        public int Losses { get; init; }

        public decimal WinRate { get; init; }

        public decimal? AverageR { get; init; }

        public decimal TotalR { get; init; }

        public int ValidKnownSetups { get; init; }

        public int ValidSetups { get; init; }

        public decimal ValidRate { get; init; }

        public decimal? AverageProcessScore { get; init; }
    }

    public class SetupReviewEditRequest
    {
        public string StrategyKey { get; set; } = string.Empty;

        public string TradeDate { get; set; } = string.Empty;

        public string TradeTimeText { get; set; } = string.Empty;

        public string? Market { get; set; }

        public string Direction { get; set; } = string.Empty;

        public string Outcome { get; set; } = string.Empty;

        public string? MissingInfo { get; set; }

        public string? Issues { get; set; }

        public string? Notes { get; set; }
    }

    public sealed class SetupReviewAnalysisResult
    {
        public int TradeId { get; set; }

        public string StrategyCode { get; set; } = string.Empty;

        public string Verdict { get; set; } = "Nelze vyhodnotit";

        public string SetupRating { get; set; } = "No trade";

        public decimal? ProcessScore { get; set; }

        public string Confidence { get; set; } = "Nízká";

        public string Summary { get; set; } = string.Empty;

        public string DirectionAssessment { get; set; } = string.Empty;

        public string BiasAssessment { get; set; } = string.Empty;

        public string HtfContext { get; set; } = string.Empty;

        public string MarketStructure { get; set; } = string.Empty;

        public string Liquidity { get; set; } = string.Empty;

        public string Zones { get; set; } = string.Empty;

        public string OrderBlock { get; set; } = string.Empty;

        public string Fvg { get; set; } = string.Empty;

        public string BalanceLevels { get; set; } = string.Empty;

        public string VolumeProfile { get; set; } = string.Empty;

        public string EntryModel { get; set; } = string.Empty;

        public string KillzoneSession { get; set; } = string.Empty;

        public string RiskReward { get; set; } = string.Empty;

        public string RiskManagement { get; set; } = string.Empty;

        public List<string> WhatWasCorrect { get; set; } = [];

        public List<string> WhatWasWrong { get; set; } = [];

        public List<string> MissingInformation { get; set; } = [];

        public List<string> NoTradeBlockers { get; set; } = [];

        public string ImprovementNotes { get; set; } = string.Empty;

        public decimal? PlannedEntry { get; set; }

        public decimal? PlannedStopLoss { get; set; }

        public decimal? PlannedTakeProfit { get; set; }

        public decimal? PlannedRRR { get; set; }

        public decimal? FinalResultR { get; set; }
    }
}
