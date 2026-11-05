using System.ComponentModel.DataAnnotations;

namespace TradingJournal.Models;

public enum PlannedTradeOutcome
{
    [Display(Name = "Zisk (TP)")]
    Win = 1,
    [Display(Name = "Ztráta (SL)")]
    Loss = 2,
    [Display(Name = "Breakeven")]
    Breakeven = 3
}
