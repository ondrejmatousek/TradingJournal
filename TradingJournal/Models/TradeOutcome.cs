namespace TradingJournal.Models
{
    public enum TradeOutcome
    {
        Unknown = 0,
        Win = 1,
        Loss = 2,
        Breakeven = 3,
        [System.ComponentModel.DataAnnotations.Display(Name = "Zavřeno ručně")]
        Manual = 4
    }
}
