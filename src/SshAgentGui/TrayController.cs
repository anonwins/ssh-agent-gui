using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SshAgentGui;

internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AgentSession _session;
    private readonly Action _show;
    private readonly Action _exit;
    private readonly ToolStripMenuItem _countItem;
    private bool _disposed;

    public TrayController(AgentSession session, Action show, Action exit)
    {
        _session = session;
        _show = show;
        _exit = exit;

        _countItem = new ToolStripMenuItem(_session.LoadedCountText) { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(_countItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open", null, (_, _) => _show()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => _exit()));

        _icon = new NotifyIcon
        {
            Visible = true,
            Text = TooltipText(),
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _show();
        _session.PropertyChanged += OnSessionPropertyChanged;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AgentSession.LoadedCount) or nameof(AgentSession.LoadedCountText)))
            return;

        void Update()
        {
            _countItem.Text = _session.LoadedCountText;
            _icon.Text = TooltipText();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Update();
        else
            dispatcher.BeginInvoke(Update);
    }

    private string TooltipText()
    {
        var text = "SSH Agent — " + _session.LoadedCountText;
        return text.Length <= 63 ? text : text[..63];
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
        _icon.Visible = false;
        _icon.Dispose();
    }
}
