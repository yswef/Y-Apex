// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using YApex.Core.Domain;
using YApex.Core.Protocol;
using YApex.Core.Security;
using YApex.Core.Services;
using YApex.UI.Models;
using YApex.UI.Services;
using YApex.UI.Settings;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace YApex.UI;

public partial class MainWindow : Window
{
    private readonly PipeClient _pipeClient = new();
    private readonly ObservableCollection<ConnectionRow> _connections = [];
    private readonly ObservableCollection<FirewallRuleRow> _rules = [];
    private readonly ObservableCollection<UsageRow> _usageRows = [];
    private readonly ObservableCollection<JournalRow> _journalRows = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SharedLinks _sharedLinks;
    private readonly DispatcherTimer _refreshTimer;
    private readonly ICollectionView _connectionView;
    private readonly string? _currentExecutablePath = GetCurrentExecutablePath();
    private UiSettings _settings;
    private TrayIconController? _trayIcon;
    private readonly List<UiOperation> _operations = [];
    private Guid? _connectionSnapshotId;
    private int _connectionOffset;
    private bool _connectionHasMore;
    private DateTimeOffset _lastRulesRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastUsageRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastJournalRefresh = DateTimeOffset.MinValue;
    private bool _loadingSettings;
    private bool _serviceAvailable;
    private bool _operationRequestInFlight;
    private bool _rulesAvailable;

    public MainWindow(UiSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        _sharedLinks = SharedLinks.Load();
        _connectionView = CollectionViewSource.GetDefaultView(_connections);
        _connectionView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConnectionRow.GroupKey)));
        _connectionView.Filter = FilterConnection;
        ConnectionsGrid.ItemsSource = _connectionView;
        RulesGrid.ItemsSource = _rules;
        UsageGrid.ItemsSource = _usageRows;
        JournalGrid.ItemsSource = _journalRows;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        VersionText.Text = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "0.1.0";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        MicaBackdrop.TryEnable(this);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _loadingSettings = true;
        SelectComboItem(LanguageCombo, _settings.Language);
        SelectComboItem(ThemeCombo, _settings.Theme);
        _loadingSettings = false;
        BuildWalletButtons();
        _trayIcon = new TrayIconController(this, EmergencyRemoveAsync);
        _refreshTimer.Start();
        await RefreshConnectionsAsync();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _refreshTimer.Interval = TimeSpan.FromSeconds(15);
            Hide();
        }
        else
        {
            _refreshTimer.Interval = TimeSpan.FromSeconds(3);
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        _trayIcon?.Dispose();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshConnectionsAsync();
        if (NavigationTabs.SelectedIndex == 2)
        {
            await RefreshUsageIfNeededAsync();
        }
        else if (NavigationTabs.SelectedIndex == 3)
        {
            await RefreshJournalIfNeededAsync();
        }
    }

    private async void NavigationTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavigationTabs.SelectedIndex == 2)
        {
            await RefreshUsageIfNeededAsync(force: true);
        }
        else if (NavigationTabs.SelectedIndex == 3)
        {
            await RefreshJournalIfNeededAsync(force: true);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _connectionOffset = 0;
        _connectionSnapshotId = null;
        await RefreshConnectionsAsync();
    }

    private async void RefreshLogs_Click(object sender, RoutedEventArgs e) => await RefreshJournalIfNeededAsync(force: true);

    private async void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        _connectionOffset = Math.Max(0, _connectionOffset - PipeProtocol.MaximumConnectionPageSize);
        if (_connectionOffset == 0)
        {
            _connectionSnapshotId = null;
        }

        await RefreshConnectionsAsync();
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (!_connectionHasMore || _connectionSnapshotId is null)
        {
            return;
        }

        _connectionOffset = checked(_connectionOffset + PipeProtocol.MaximumConnectionPageSize);
        await RefreshConnectionsAsync();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => _connectionView.Refresh();

    private UiOperation? CurrentOperation => _operations.Count == 0 ? null : _operations[^1];

    private void UpdateOperationButtons()
    {
        var operation = CurrentOperation;
        var hasUnconfirmedOperation = _operations.Any(item => !item.ConnectivityConfirmed);
        UndoButton.IsEnabled = _serviceAvailable && operation is not null && !_operationRequestInFlight;
        ConfirmButton.IsEnabled = _serviceAvailable && operation is { ConnectivityConfirmed: false } && !_operationRequestInFlight;
        AllowButton.IsEnabled = _serviceAvailable && !hasUnconfirmedOperation && !_operationRequestInFlight;
        BlockButton.IsEnabled = _serviceAvailable && !hasUnconfirmedOperation && !_operationRequestInFlight;
        EmergencyButton.IsEnabled = _serviceAvailable && !_operationRequestInFlight;
    }

    private void RemoveCurrentOperation(Guid operationId)
    {
        _operations.RemoveAll(operation => operation.OperationId == operationId);
        UpdateOperationButtons();
    }

    private void MarkCurrentOperationConfirmed(Guid operationId)
    {
        var index = _operations.FindLastIndex(operation => operation.OperationId == operationId);
        if (index >= 0)
        {
            _operations[index] = _operations[index] with { ConnectivityConfirmed = true };
        }

        UpdateOperationButtons();
    }

    private async void Block_Click(object sender, RoutedEventArgs e) => await PreviewAndApplyAsync(FirewallAction.Block);

    private async void Allow_Click(object sender, RoutedEventArgs e) => await PreviewAndApplyAsync(FirewallAction.Allow);

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentOperation is not { } operation || _operationRequestInFlight)
        {
            return;
        }

        _operationRequestInFlight = true;
        UpdateOperationButtons();
        try
        {
            var result = await _pipeClient.SendAsync<UndoResponse>(
                new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.Undo, OperationId: operation.OperationId),
                CancellationToken.None);
            RemoveCurrentOperation(operation.OperationId);
            if (result.Undone)
            {
                ShowMessage(LocalizationService.Get("Reverted"));
                _lastRulesRefresh = DateTimeOffset.MinValue;
                await RefreshConnectionsAsync();
            }
            else
            {
                ShowMessage(LocalizationService.Get("UnknownError"));
            }
        }
        catch (Exception exception) when (IsExpectedServiceFailure(exception))
        {
            ShowFailure(exception);
        }
        finally
        {
            _operationRequestInFlight = false;
            UpdateOperationButtons();
        }
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentOperation is not { ConnectivityConfirmed: false } operation || _operationRequestInFlight)
        {
            return;
        }

        _operationRequestInFlight = true;
        UpdateOperationButtons();
        try
        {
            var result = await _pipeClient.SendAsync<ConfirmResponse>(
                new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.ConfirmConnectivity, OperationId: operation.OperationId),
                CancellationToken.None);
            if (result.Confirmed)
            {
                MarkCurrentOperationConfirmed(operation.OperationId);
                ShowMessage(LocalizationService.Get("Confirmed"));
            }
            else
            {
                RemoveCurrentOperation(operation.OperationId);
                ShowMessage(LocalizationService.Get("UnknownError"));
            }
        }
        catch (Exception exception) when (IsExpectedServiceFailure(exception))
        {
            ShowFailure(exception);
        }
        finally
        {
            _operationRequestInFlight = false;
            UpdateOperationButtons();
        }
    }

    private async void Emergency_Click(object sender, RoutedEventArgs e) => await EmergencyRemoveAsync();

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var exportConnections = _connections.Where(connection => FilterConnection(connection)).ToArray();
        if (exportConnections.Length == 0)
        {
            ShowMessage(LocalizationService.Get("NoConnectionsTitle"));
            return;
        }

        if (MessageBox.Show(this, LocalizationService.Get("ExportWarning"), LocalizationService.Get("ExportData"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = "y-apex-connections",
            Filter = "JSON (*.json)|*.json|CSV (*.csv)|*.csv",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                var csv = new StringBuilder();
                csv.AppendLine("Process,Path,LocalAddress,LocalPort,RemoteAddress,RemotePort,Protocol,State,Direction,Signature");
                foreach (var row in exportConnections)
                {
                    var item = row.Value;
                    csv.AppendLine(string.Join(',',
                        Csv(item.Process.Name),
                        Csv(item.Process.Path ?? string.Empty),
                        Csv(item.LocalEndpoint.Address),
                        item.LocalEndpoint.Port.ToString(CultureInfo.InvariantCulture),
                        Csv(item.RemoteEndpoint?.Address ?? string.Empty),
                        item.RemoteEndpoint?.Port.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        item.Protocol.ToString(),
                        item.State.ToString(),
                        item.Direction.ToString(),
                        item.Process.Signature.ToString()));
                }

                await File.WriteAllTextAsync(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            else
            {
                var json = JsonSerializer.Serialize(exportConnections.Select(row => row.Value), new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(dialog.FileName, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowFailure(exception);
        }
    }

    private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || LanguageCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
        {
            return;
        }

        _settings = _settings with { Language = language };
        ApplySettings();
        foreach (var connection in _connections)
        {
            connection.RefreshLocalizedProperties();
        }

        var localizedRules = _rules.Select(rule => new FirewallRuleRow(rule.Value)).ToArray();
        _rules.Clear();
        foreach (var rule in localizedRules)
        {
            _rules.Add(rule);
        }

        var localizedJournal = _journalRows.Select(row => new JournalRow(row.Value)).ToArray();
        _journalRows.Clear();
        foreach (var entry in localizedJournal)
        {
            _journalRows.Add(entry);
        }

        LogsStatusText.Text = !_serviceAvailable
            ? LocalizationService.Get("LogsUnavailable")
            : _journalRows.Count == 0
                ? LocalizationService.Get("LogsEmpty")
                : string.Format(CultureInfo.CurrentCulture, LocalizationService.Get("LogsRecentCount"), _journalRows.Count);
        BuildWalletButtons();
        ServiceStatusText.Text = LocalizationService.Get(_serviceAvailable ? "ServiceRunning" : "ServiceStopped");
        ConnectionHintText.Text = _serviceAvailable
            ? (_connections.Count == 0 ? LocalizationService.Get("NoConnectionsText") : string.Empty)
            : LocalizationService.Get("NoService");
        if (_trayIcon is not null)
        {
            _trayIcon.RefreshMenu();
        }

        if (NavigationTabs.SelectedIndex == 2)
        {
            await RefreshUsageIfNeededAsync(force: true);
        }
        else if (NavigationTabs.SelectedIndex == 3)
        {
            await RefreshJournalIfNeededAsync(force: true);
        }
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || ThemeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string theme)
        {
            return;
        }

        _settings = _settings with { Theme = theme };
        ApplySettings();
    }

    private void SupportLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
        {
            return;
        }

        var address = key switch
        {
            "buyMeACoffee" => _sharedLinks.BuyMeACoffee,
            "issues" => _sharedLinks.Issues,
            "changelog" => _sharedLinks.Changelog,
            "license" => _sharedLinks.License,
            "privacy" => _sharedLinks.Privacy,
            "repo" => _sharedLinks.Repository,
            _ => null
        };
        CopyConfiguredAddress(address);
    }

    private void WalletButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WalletLink wallet })
        {
            Clipboard.SetText(wallet.Address);
            ShowMessage(LocalizationService.Get("WalletCopied"));
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var result = await new OfflineUpdateChecker().CheckForUpdatesAsync(CancellationToken.None);
        ShowMessage(result.Message);
    }

    private async Task RefreshConnectionsAsync()
    {
        if (!_refreshGate.Wait(0))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var offset = _connectionOffset;
            var request = new PipeRequest(
                PipeProtocol.CurrentVersion,
                Guid.NewGuid(),
                PipeCommands.GetConnections,
                Offset: offset,
                Limit: PipeProtocol.MaximumConnectionPageSize,
                SnapshotId: offset == 0 ? null : _connectionSnapshotId);
            ConnectionPage page;
            try
            {
                page = await _pipeClient.SendAsync<ConnectionPage>(request, timeout.Token);
            }
            catch (Exception exception) when (offset > 0 && IsExpectedServiceFailure(exception))
            {
                // A page snapshot may expire while the user is on a later page. Start a fresh snapshot.
                _connectionOffset = 0;
                _connectionSnapshotId = null;
                request = request with { RequestId = Guid.NewGuid(), Offset = 0, SnapshotId = null };
                page = await _pipeClient.SendAsync<ConnectionPage>(request, timeout.Token);
            }

            _connectionOffset = page.Offset;
            _connectionSnapshotId = page.SnapshotId;
            _connectionHasMore = page.HasMore;
            _connections.Clear();
            foreach (var record in page.Connections.OrderBy(item => item.Process.Name, StringComparer.OrdinalIgnoreCase))
            {
                _connections.Add(new ConnectionRow(record));
            }

            _connectionView.Refresh();
            _serviceAvailable = true;
            ServiceStatusText.Text = LocalizationService.Get("ServiceRunning");
            UpdateOperationButtons();
            UpdateConnectionPageStatus(page);
            await RefreshRulesIfNeededAsync(timeout.Token);
            await RefreshOperationsAsync(timeout.Token);
            await CheckFirstConnectionPromptsAsync();
            UpdateTrayState();
        }
        catch (Exception exception) when (IsExpectedServiceFailure(exception))
        {
            _serviceAvailable = false;
            UpdateOperationButtons();
            _connectionSnapshotId = null;
            _connections.Clear();
            _connectionView.Refresh();
            ServiceStatusText.Text = LocalizationService.Get("ServiceStopped");
            ConnectionHintText.Text = LocalizationService.Get("NoService");
            PageStatusText.Text = string.Empty;
            PreviousPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            _trayIcon?.SetState("warning");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void UpdateConnectionPageStatus(ConnectionPage page)
    {
        PreviousPageButton.IsEnabled = page.Offset > 0;
        NextPageButton.IsEnabled = page.HasMore;
        if (page.TotalCount == 0)
        {
            ConnectionHintText.Text = LocalizationService.Get("NoConnectionsText");
            PageStatusText.Text = page.IsTruncated ? LocalizationService.Get("ConnectionsTruncated") : string.Empty;
            return;
        }

        var first = page.Offset + 1;
        var last = page.Offset + page.Connections.Count;
        var total = page.IsTruncated ? $"{page.TotalCount:N0}+" : page.TotalCount.ToString("N0", CultureInfo.CurrentCulture);
        PageStatusText.Text = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationService.Get("ConnectionsPage"),
            first,
            last,
            total);
        ConnectionHintText.Text = page.IsTruncated ? LocalizationService.Get("ConnectionsTruncated") : string.Empty;
    }

    private async Task RefreshRulesIfNeededAsync(CancellationToken cancellationToken, bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _lastRulesRefresh < TimeSpan.FromSeconds(15))
        {
            return;
        }

        try
        {
            var rules = await _pipeClient.SendAsync<ManagedFirewallRule[]>(PipeCommands.GetRules, cancellationToken);
            _rules.Clear();
            foreach (var rule in rules)
            {
                _rules.Add(new FirewallRuleRow(rule));
            }

            _rulesAvailable = true;
            _lastRulesRefresh = DateTimeOffset.UtcNow;
        }
        catch (Exception exception) when (IsExpectedServiceFailure(exception))
        {
            _rules.Clear();
            _rulesAvailable = false;
        }
    }

    private async Task RefreshOperationsAsync(CancellationToken cancellationToken)
    {
        var operations = await _pipeClient.SendAsync<ActiveRuleOperation[]>(PipeCommands.GetOperations, cancellationToken);
        if (_operationRequestInFlight)
        {
            return;
        }

        _operations.Clear();
        _operations.AddRange(operations.Select(operation => new UiOperation(operation.OperationId, operation.ConnectivityConfirmed)));
        UpdateOperationButtons();
    }

    private async Task CheckFirstConnectionPromptsAsync()
    {
        if (!_serviceAvailable || !_rulesAvailable || _operationRequestInFlight || _operations.Any(operation => !operation.ConnectivityConfirmed))
        {
            return;
        }

        var knownHashes = new HashSet<string>(
            _settings.SeenProgramPathHashes ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var connection in _connections)
        {
            if (string.IsNullOrWhiteSpace(connection.Value.Process.Path) ||
                (_currentExecutablePath is not null && string.Equals(connection.Value.Process.Path, _currentExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string normalizedPath;
            try
            {
                normalizedPath = WindowsProgramPath.Normalize(connection.Value.Process.Path);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var pathHash = WindowsProgramPath.GetStableHash(normalizedPath);
            if (knownHashes.Contains(pathHash))
            {
                continue;
            }

            await RefreshRulesIfNeededAsync(CancellationToken.None, force: true);
            if (!_rulesAvailable)
            {
                return;
            }

            if (_rules.Any(rule => string.Equals(rule.Value.ProgramPath, normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                RememberProgramPath(pathHash);
                break;
            }

            RememberProgramPath(pathHash);
            var prompt = string.Join(
                Environment.NewLine,
                string.Format(CultureInfo.CurrentCulture, LocalizationService.Get("FirstConnectionPrompt"), connection.ProcessName, normalizedPath),
                LocalizationService.Get("FirstConnectionOptions"));
            var choice = MessageBox.Show(
                this,
                prompt,
                LocalizationService.Get("ConfirmTitle"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);
            if (choice is MessageBoxResult.Yes or MessageBoxResult.No)
            {
                ConnectionsGrid.SelectedItem = connection;
                await PreviewAndApplyAsync(choice == MessageBoxResult.Yes ? FirewallAction.Allow : FirewallAction.Block, connection);
            }

            break;
        }
    }

    private void RememberProgramPath(string pathHash)
    {
        var hashes = (_settings.SeenProgramPathHashes ?? Array.Empty<string>())
            .Where(hash => !string.Equals(hash, pathHash, StringComparison.OrdinalIgnoreCase))
            .Append(pathHash)
            .TakeLast(200)
            .ToArray();
        _settings = _settings with { SeenProgramPathHashes = hashes };
        try
        {
            UiSettingsStore.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowFailure(exception);
        }
    }

    private async Task RefreshUsageIfNeededAsync(bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _lastUsageRefresh < TimeSpan.FromSeconds(15))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var snapshot = await _pipeClient.SendAsync<UsageSnapshot>(PipeCommands.GetUsage, timeout.Token);
            TotalSentText.Text = UsageRow.FormatBytes(snapshot.BytesSent);
            TotalReceivedText.Text = UsageRow.FormatBytes(snapshot.BytesReceived);
            _usageRows.Clear();
            var maximum = snapshot.Programs.Count == 0 ? 0 : snapshot.Programs.Max(program => program.TotalBytes);
            foreach (var program in snapshot.Programs)
            {
                _usageRows.Add(new UsageRow(program, maximum));
            }

            var status = new List<string>();
            status.Add(LocalizationService.Get(snapshot.IsAvailable ? "EtwCollecting" : "EtwUnavailable"));
            if (snapshot.IsAvailable && snapshot.Programs.Count == 0)
            {
                status.Add(LocalizationService.Get("UsageEmpty"));
            }

            if (snapshot.IsTruncated)
            {
                status.Add(LocalizationService.Get("UsageTruncated"));
            }

            UsageStatusText.Text = string.Join(" ", status);
            _lastUsageRefresh = DateTimeOffset.UtcNow;
        }
        catch (Exception exception) when (IsExpectedServiceFailure(exception))
        {
            TotalSentText.Text = "—";
            TotalReceivedText.Text = "—";
            _usageRows.Clear();
            UsageStatusText.Text = LocalizationService.Get("EtwUnavailable");
        }
    }

    private async Task RefreshJournalIfNeededAsync(bool force = false)
    {
        if (!force && DateTimeOffset.UtcNow - _lastJournalRefresh < TimeSpan.FromSeconds(15))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var entries = await _pipeClient.SendAsync<ChangeJournalSummary[]>(PipeCommands.GetJournal, timeout.Token);
            _journalRows.Clear();
            foreach (var entry in entries)
            {
                _journalRows.Add(new JournalRow(entry));
            }

            LogsStatusText.Text = entries.Length == 0
                ? LocalizationService.Get("LogsEmpty")
                : string.Format(CultureInfo.CurrentCulture, LocalizationService.Get("LogsRecentCount"), entries.Length);
            _lastJournalRefresh = DateTimeOffset.UtcNow;
        }
        catch (Exception exception) when (IsExpectedServiceFailure(exception))
        {
            _journalRows.Clear();
            LogsStatusText.Text = LocalizationService.Get("LogsUnavailable");
        }
    }

    private async void ExportJournal_Click(object sender, RoutedEventArgs e)
    {
        if (_journalRows.Count == 0)
        {
            ShowMessage(LocalizationService.Get("LogsEmpty"));
            return;
        }

        if (MessageBox.Show(this, LocalizationService.Get("ExportJournalWarning"), LocalizationService.Get("ExportData"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = "y-apex-activity",
            Filter = "CSV (*.csv)|*.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,State,ProgramPath,Reason");
            foreach (var row in _journalRows)
            {
                csv.AppendLine(string.Join(',',
                    Csv(row.Value.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(row.StateText),
                    Csv(row.ProgramPath),
                    Csv(row.ReasonText)));
            }

            await File.WriteAllTextAsync(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowFailure(exception);
        }
    }

    private async Task PreviewAndApplyAsync(FirewallAction action, ConnectionRow? connectionOverride = null)
    {
        if (_operationRequestInFlight)
        {
            return;
        }

        if (_operations.Any(operation => !operation.ConnectivityConfirmed))
        {
            ShowMessage(LocalizationService.Get("ResolvePendingOperation"));
            return;
        }

        var selected = connectionOverride ?? (ConnectionsGrid.SelectedItem as ConnectionRow);
        if (selected is null || string.IsNullOrWhiteSpace(selected.Value.Process.Path))
        {
            ShowMessage(LocalizationService.Get("NoSelection"));
            return;
        }

        _operationRequestInFlight = true;
        UpdateOperationButtons();
        try
        {
            var preview = await _pipeClient.SendAsync<RuleChangePreview>(
                new PipeRequest(
                    PipeProtocol.CurrentVersion,
                    Guid.NewGuid(),
                    PipeCommands.PreviewRule,
                    ProgramPath: selected.Value.Process.Path,
                    Action: action == FirewallAction.Block ? "block" : "allow"),
                CancellationToken.None);
            var actionText = LocalizationService.Get(action == FirewallAction.Block ? "Block" : "Allow");
            var publisher = string.IsNullOrWhiteSpace(selected.Value.Process.Publisher) ? string.Empty : $"\n{selected.Value.Process.Publisher}";
            var message = string.Join(Environment.NewLine,
                $"{LocalizationService.Get("Program")}: {selected.ProcessName}",
                $"{LocalizationService.Get("Path")}: {selected.ProgramPath}",
                $"{LocalizationService.Get("Signature")}: {selected.SignatureText}{publisher}",
                $"{LocalizationService.Get("PreviewCurrentState")}: {preview.Before.Count}",
                $"{LocalizationService.Get("PreviewNewState")}: {actionText} — {preview.After.Count}",
                LocalizationService.Get("PreviewWarning"),
                LocalizationService.Get("RollbackWarning"));
            if (MessageBox.Show(this, message, LocalizationService.Get("RulePreview"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await _pipeClient.SendAsync<RuleChangeResult>(
                new PipeRequest(
                    PipeProtocol.CurrentVersion,
                    Guid.NewGuid(),
                    PipeCommands.ApplyRule,
                    PreviewId: preview.PreviewId,
                    Confirmed: true),
                CancellationToken.None);
            _operations.Add(new UiOperation(result.OperationId, false));
            UpdateOperationButtons();
            _lastRulesRefresh = DateTimeOffset.MinValue;
            await RefreshRulesIfNeededAsync(CancellationToken.None, force: true);
            UpdateTrayState();
            var response = MessageBox.Show(
                this,
                LocalizationService.Get("Applied") + Environment.NewLine + LocalizationService.Get("RollbackWarning"),
                LocalizationService.Get("ConfirmTitle"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);
            if (response == MessageBoxResult.Yes)
            {
                await ConfirmCurrentOperationAsync();
            }
            else if (response == MessageBoxResult.No)
            {
                await UndoCurrentOperationAsync();
            }
        }
        catch (Exception exception) when (IsExpectedServiceFailure(exception))
        {
            ShowFailure(exception);
        }
        finally
        {
            _operationRequestInFlight = false;
            UpdateOperationButtons();
        }
    }

    private async Task ConfirmCurrentOperationAsync()
    {
        if (CurrentOperation is not { ConnectivityConfirmed: false } operation)
        {
            return;
        }

        var result = await _pipeClient.SendAsync<ConfirmResponse>(
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.ConfirmConnectivity, OperationId: operation.OperationId),
            CancellationToken.None);
        if (result.Confirmed)
        {
            MarkCurrentOperationConfirmed(operation.OperationId);
            ShowMessage(LocalizationService.Get("Confirmed"));
        }
        else
        {
            RemoveCurrentOperation(operation.OperationId);
            ShowMessage(LocalizationService.Get("UnknownError"));
        }
    }

    private async Task UndoCurrentOperationAsync()
    {
        if (CurrentOperation is not { } operation)
        {
            return;
        }

        var result = await _pipeClient.SendAsync<UndoResponse>(
            new PipeRequest(PipeProtocol.CurrentVersion, Guid.NewGuid(), PipeCommands.Undo, OperationId: operation.OperationId),
            CancellationToken.None);
        RemoveCurrentOperation(operation.OperationId);
        if (result.Undone)
        {
            ShowMessage(LocalizationService.Get("Reverted"));
            _lastRulesRefresh = DateTimeOffset.MinValue;
            await RefreshConnectionsAsync();
        }
        else
        {
            ShowMessage(LocalizationService.Get("UnknownError"));
        }
    }

    private async Task EmergencyRemoveAsync()
    {
        if (_operationRequestInFlight)
        {
            return;
        }

        _operationRequestInFlight = true;
        UpdateOperationButtons();
        try
        {
            var preview = await _pipeClient.SendAsync<EmergencyRemovalPreview>(
                PipeCommands.EmergencyPreview,
                CancellationToken.None);
            var sample = preview.SampleRules.Select(rule =>
                $"{rule.ProgramPath} — {LocalizationService.Get($"Direction{rule.Direction}")} — {LocalizationService.Get(rule.Action.ToString())}");
            var details = string.Join(Environment.NewLine,
                string.Format(CultureInfo.CurrentCulture, LocalizationService.Get("EmergencyPreviewCount"), preview.RuleCount),
                string.Join(Environment.NewLine, sample),
                preview.RuleCount > preview.SampleRules.Count
                    ? string.Format(CultureInfo.CurrentCulture, LocalizationService.Get("EmergencyAdditionalRules"), preview.RuleCount - preview.SampleRules.Count)
                    : string.Empty,
                LocalizationService.Get("Emergency"));
            if (MessageBox.Show(this, details, LocalizationService.Get("ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            var result = await _pipeClient.SendAsync<EmergencyResponse>(
                new PipeRequest(
                    PipeProtocol.CurrentVersion,
                    Guid.NewGuid(),
                    PipeCommands.EmergencyRemove,
                    PreviewId: preview.PreviewId,
                    Confirmed: true),
                CancellationToken.None);
            _operations.Clear();
            UpdateOperationButtons();
            _lastRulesRefresh = DateTimeOffset.MinValue;
            UpdateTrayState();
            ShowMessage(string.Format(CultureInfo.CurrentCulture, LocalizationService.Get("EmergencyDone"), result.RemovedRules));
            _connectionOffset = 0;
            _connectionSnapshotId = null;
            await RefreshConnectionsAsync();
        }
        catch (Exception exception) when (IsExpectedServiceFailure(exception))
        {
            ShowFailure(exception);
        }
        finally
        {
            _operationRequestInFlight = false;
            UpdateOperationButtons();
        }
    }

    private void ApplySettings()
    {
        LocalizationService.Apply(_settings.Language);
        ThemeService.Apply(_settings.Theme);
        try
        {
            UiSettingsStore.Save(_settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowFailure(exception);
        }
    }

    private void BuildWalletButtons()
    {
        WalletsPanel.Children.Clear();
        if (_sharedLinks.Wallets.Count == 0)
        {
            WalletsPanel.Children.Add(new TextBlock { Text = LocalizationService.Get("NoWallets"), Margin = new Thickness(6) });
            return;
        }

        foreach (var wallet in _sharedLinks.Wallets)
        {
            var button = new Button
            {
                Content = $"{wallet.Name} — {LocalizationService.Get("CopyWallet")}",
                Tag = wallet,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Click += WalletButton_Click;
            WalletsPanel.Children.Add(button);
        }
    }

    private void CopyConfiguredAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            ShowMessage(LocalizationService.Get("LinksMissing"));
            return;
        }

        Clipboard.SetText(address);
        ShowMessage(LocalizationService.Get("OpenLinkWarning") + Environment.NewLine + address);
    }

    private void UpdateTrayState(bool? requestedBlock = null)
    {
        var blocked = requestedBlock ?? _rules.Any(rule => rule.Value.Action == FirewallAction.Block && rule.Enabled);
        _trayIcon?.SetState(!_serviceAvailable ? "warning" : blocked ? "blocked" : "online");
    }

    private void ShowFailure(Exception exception)
    {
        var message = exception is InvalidOperationException && !string.IsNullOrWhiteSpace(exception.Message)
            ? exception.Message
            : LocalizationService.Get("UnknownError");
        MessageBox.Show(this, message, LocalizationService.Get("DialogTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ShowMessage(string message) =>
        MessageBox.Show(this, message, LocalizationService.Get("DialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

    private bool FilterConnection(object item)
    {
        if (item is not ConnectionRow connection || string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            return true;
        }

        var query = SearchBox.Text.Trim();
        return connection.ProcessName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               connection.ProgramPath.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               connection.RemoteAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               connection.ProtocolText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectComboItem(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string? GetCurrentExecutablePath()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsExpectedServiceFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or TimeoutException or OperationCanceledException or ArgumentException or JsonException or YApex.Core.Protocol.PipeProtocolException;

    private sealed record UndoResponse(bool Undone);

    private sealed record ConfirmResponse(bool Confirmed);

    private sealed record EmergencyResponse(int RemovedRules);

    private sealed record UiOperation(Guid OperationId, bool ConnectivityConfirmed);
}
