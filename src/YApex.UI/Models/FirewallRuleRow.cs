// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.IO;
using YApex.Core.Domain;
using YApex.UI.Settings;

namespace YApex.UI.Models;

public sealed record FirewallRuleRow(ManagedFirewallRule Value)
{
    public string ProgramName => Path.GetFileName(Value.ProgramPath);

    public string ProgramPath => Value.ProgramPath;

    public string DirectionText => LocalizationService.Get("Direction" + Value.Direction);

    public string ActionText => LocalizationService.Get(Value.Action == FirewallAction.Block ? "Block" : "Allow");

    public bool Enabled => Value.Enabled;
}
