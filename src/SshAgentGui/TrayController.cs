using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SshAgentGui;

internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AgentSession _session;
    private readonly Action _show;
    private readonly Func<Task> _exit;
    private readonly Func<Task> _unloadAll;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _countItem;
    private readonly ToolStripMenuItem _unloadItem;
    private bool _disposed;

    public TrayController(AgentSession session, Action show, Func<Task> exit, Func<Task> unloadAll)
    {
        _session = session;
        _show = show;
        _exit = exit;
        _unloadAll = unloadAll;

        _statusItem = new ToolStripMenuItem(Trim(_session.StatusText, 60)) { Enabled = false };
        _countItem = new ToolStripMenuItem(_session.LoadedCountText) { Enabled = false };
        _unloadItem = new ToolStripMenuItem("Unload all", null, (_, _) => RunOnDispatcher(_unloadAll));

        var menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(_countItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_unloadItem);
        menu.Items.Add(new ToolStripMenuItem("Open", null, (_, _) => _show()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => RunOnDispatcher(_exit)));

        _icon = new NotifyIcon
        {
            Visible = true,
            Text = TooltipText(),
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += OnMouseClick;
        _session.PropertyChanged += OnSessionPropertyChanged;
        UpdateMenu();
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            _show();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
            nameof(AgentSession.StatusText)
            or nameof(AgentSession.IsBusy)
            or nameof(AgentSession.IsIdle)
            or nameof(AgentSession.LoadedCount)
            or nameof(AgentSession.LoadedCountText)
            or nameof(AgentSession.IsAgentUnavailable)))
            return;

        void Update()
        {
            if (_disposed)
                return;
            UpdateMenu();
            _icon.Text = TooltipText();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Update();
        else
            dispatcher.BeginInvoke(Update);
    }

    private void UpdateMenu()
    {
        _statusItem.Text = Trim(_session.StatusText, 60);
        _countItem.Text = _session.LoadedCountText;
        _unloadItem.Enabled = _session.LoadedCount > 0 && _session.IsIdle;
    }

    private string TooltipText()
    {
        var text = "SSH Agent GUI — " + _session.LoadedCountText;
        return text.Length <= 63 ? text : text[..63];
    }

    private static void RunOnDispatcher(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _ = action();
            return;
        }

        dispatcher.BeginInvoke(async () => await action().ConfigureAwait(true));
    }

    private static string Trim(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text;
        return text[..max];
    }

    private static Icon LoadIcon()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                    return icon;
            }
            catch
            {
                // fall back
            }
        }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _session.PropertyChanged -= OnSessionPropertyChanged;
        _icon.MouseClick -= OnMouseClick;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
