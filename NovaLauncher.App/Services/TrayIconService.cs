using System.IO;
using System.Windows;
using NovaLauncher.App.Diagnostics;
using Forms = System.Windows.Forms;

namespace NovaLauncher.App.Services;

public enum TrayAction { Open, QuickLauncher, Settings, Exit }
public interface ITrayIconService : IDisposable
{
    bool IsVisible { get; }
    event Action<TrayAction>? Requested;
    void Initialize();
    void ShowBackgroundHint(string message);
}

public sealed class TrayIconService(NovaLog? log = null) : ITrayIconService
{
    private Forms.NotifyIcon? _icon;
    private Forms.ContextMenuStrip? _menu;
    private System.Drawing.Icon? _brand;
    public bool IsVisible => _icon?.Visible == true;
    public event Action<TrayAction>? Requested;
    public void Initialize()
    {
        if (_icon is not null) return;
        try
        {
            using Stream stream = Application.GetResourceStream(new Uri("pack://application:,,,/NovaLauncher;component/Resources/Nova.ico"))!.Stream;
            _brand = new System.Drawing.Icon(stream);
            _menu = new Forms.ContextMenuStrip();
            Add("打开 Nova", TrayAction.Open);
            Add("快速启动", TrayAction.QuickLauncher);
            _menu.Items.Add(new Forms.ToolStripSeparator());
            Add("设置", TrayAction.Settings);
            _menu.Items.Add(new Forms.ToolStripSeparator());
            Add("退出 Nova", TrayAction.Exit);
            _icon = new Forms.NotifyIcon { Icon = _brand, Text = "Nova Launcher", ContextMenuStrip = _menu };
            _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Requested?.Invoke(TrayAction.Open); };
            _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Requested?.Invoke(TrayAction.Open); };
            _icon.Visible = true;
            log?.Write(LogLevel.Information, "Tray", "Tray initialized");
        }
        catch { Dispose(); throw; }
    }
    private void Add(string text, TrayAction action) => _menu!.Items.Add(text, null, (_, _) => Requested?.Invoke(action));
    public void ShowBackgroundHint(string message) => _icon?.ShowBalloonTip(5000, "Nova 正在后台运行", message, Forms.ToolTipIcon.Info);
    public void Dispose()
    {
        if (_icon is not null)
        {
            _icon.Visible = false; _icon.Dispose(); _icon = null;
            log?.Write(LogLevel.Information, "Tray", "Tray disposed");
        }
        _menu?.Dispose(); _menu = null;
        _brand?.Dispose(); _brand = null;
    }
}
