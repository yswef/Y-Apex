// © ENG.YSWEF — 170477919+yswef@users.noreply.github.com
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using YApex.UI.Settings;

namespace YApex.UI.Services;

public sealed class TrayIconController : IDisposable
{
    private readonly Window _window;
    private readonly Func<Task> _emergencyAction;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _openItem;
    private readonly Forms.ToolStripMenuItem _emergencyItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private Icon? _ownedIcon;

    public TrayIconController(Window window, Func<Task> emergencyAction)
    {
        _window = window;
        _emergencyAction = emergencyAction;
        _menu = new Forms.ContextMenuStrip();
        _openItem = new Forms.ToolStripMenuItem();
        _emergencyItem = new Forms.ToolStripMenuItem();
        _exitItem = new Forms.ToolStripMenuItem();
        _openItem.Click += (_, _) => ShowWindow();
        _emergencyItem.Click += async (_, _) => await _emergencyAction().ConfigureAwait(true);
        _exitItem.Click += (_, _) => _window.Close();
        RefreshMenu();
        _menu.Items.Add(_openItem);
        _menu.Items.Add(_emergencyItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_exitItem);
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Y-Apex",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
        SetState("warning");
    }

    public void RefreshMenu()
    {
        _openItem.Text = LocalizationService.Get("AppTitle");
        _emergencyItem.Text = LocalizationService.Get("Emergency");
        _exitItem.Text = LocalizationService.Get("Exit");
    }

    public void SetState(string state)
    {
        var fileName = state switch
        {
            "online" => "tray-online.ico",
            "blocked" => "tray-blocked.ico",
            _ => "tray-warning.ico"
        };
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "icons", "generated", fileName);
        _ownedIcon?.Dispose();
        _ownedIcon = File.Exists(path) ? new Icon(path) : null;
        _notifyIcon.Icon = _ownedIcon ?? SystemIcons.Application;
        _notifyIcon.Text = state switch
        {
            "online" => "Y-Apex — online",
            "blocked" => "Y-Apex — rules active",
            _ => "Y-Apex — service unavailable"
        };
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _ownedIcon?.Dispose();
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
