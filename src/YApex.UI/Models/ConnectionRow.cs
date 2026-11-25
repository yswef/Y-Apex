// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.ComponentModel;
using System.Runtime.CompilerServices;
using YApex.Core.Domain;
using YApex.UI.Settings;

namespace YApex.UI.Models;

public sealed class ConnectionRow(ConnectionRecord value) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ConnectionRecord Value { get; } = value;

    public string ProcessName => string.IsNullOrWhiteSpace(Value.Process.Name) ? LocalizationService.Get("Program") : Value.Process.Name;

    public string GroupKey => Value.Process.Path ?? ProcessName;

    public string ProgramPath => Value.Process.Path ?? LocalizationService.Get("UnknownPath");

    public string LocalAddress => Value.LocalEndpoint.Address;

    public string LocalPort => Value.LocalEndpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string RemoteAddress => Value.RemoteEndpoint?.Address ?? LocalizationService.Get("RemoteUnavailable");

    public string RemotePort => Value.RemoteEndpoint?.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? LocalizationService.Get("RemoteUnavailable");

    public string ProtocolText => Value.Protocol.ToString().ToUpperInvariant();

    public string StateText => LocalizationService.Get("State" + Value.State);

    public string DirectionText => LocalizationService.Get("Direction" + Value.Direction);

    public string SignatureText => Value.Process.Signature switch
    {
        SignatureState.Valid => LocalizationService.Get("SignatureValid"),
        SignatureState.NotSigned => LocalizationService.Get("SignatureUnsigned"),
        SignatureState.Invalid => LocalizationService.Get("SignatureInvalid"),
        _ => LocalizationService.Get("StateUnknown")
    };

    public void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(ProcessName));
        OnPropertyChanged(nameof(ProgramPath));
        OnPropertyChanged(nameof(RemoteAddress));
        OnPropertyChanged(nameof(RemotePort));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(DirectionText));
        OnPropertyChanged(nameof(SignatureText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
