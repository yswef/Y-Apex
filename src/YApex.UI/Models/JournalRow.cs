// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Globalization;
using YApex.Core.Domain;
using YApex.UI.Settings;

namespace YApex.UI.Models;

public sealed class JournalRow(ChangeJournalSummary value)
{
    public ChangeJournalSummary Value { get; } = value;

    public string TimestampText => Value.Timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string StateText => LocalizationService.Get($"JournalState{Value.State}");

    public string ProgramPath => Value.ProgramPath ?? LocalizationService.Get("AllPrograms");

    public string ReasonText => Value.Reason switch
    {
        "user-confirmed" => LocalizationService.Get("JournalReasonChange"),
        "awaiting-connectivity-confirmation" => LocalizationService.Get("JournalReasonAwaitingConnectivity"),
        "user-confirmed-connectivity" => LocalizationService.Get("JournalReasonConnectivityConfirmed"),
        "automatic-rollback" or "connectivity-not-confirmed" => LocalizationService.Get("JournalReasonAutomaticRollback"),
        "user-requested-undo" => LocalizationService.Get("JournalReasonUndo"),
        "emergency-remove-confirmed" => LocalizationService.Get("JournalReasonEmergencyRemoval"),
        "startup-recovery" => LocalizationService.Get("JournalReasonStartupRecovery"),
        _ => Value.Reason
    };
}
