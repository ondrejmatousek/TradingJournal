using TradingJournal.Models;

namespace TradingJournal.Services
{
    public static class EmaTimeframeParser
    {
        public const EmaTimeframe Default = EmaTimeframe.Weekly;

        public static EmaTimeframe Parse(string? value)
        {
            return Enum.TryParse<EmaTimeframe>(value, ignoreCase: true, out var timeframe)
                ? timeframe
                : Default;
        }

        public static string ToLabel(EmaTimeframe timeframe)
        {
            return timeframe == EmaTimeframe.Weekly ? "Weekly EMA 200" : "Daily EMA 200";
        }

        public static string ToCompactLabel(EmaTimeframe timeframe)
        {
            return timeframe == EmaTimeframe.Weekly ? "Weekly EMA200" : "Daily EMA200";
        }
    }
}
