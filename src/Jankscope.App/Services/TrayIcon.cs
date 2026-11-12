namespace Jankscope.App.Services;

/// <summary>
/// The notification-area icon: the program's presence while the window is closed, its state, and the mark fallback (spec section 7.3: show the chord in the
/// tray, offer a "Mark" item when a hotkey cannot be registered). This is the one class that uses Windows Forms, always with full names.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    /// <summary>Windows cuts a notification-area tooltip at 63 characters.</summary>
    internal const int MaxTipLength = 63;

    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly Dictionary<TrayState, System.Drawing.Icon> _icons;
    private readonly System.Windows.Forms.ToolStripMenuItem _stutter;
    private readonly System.Windows.Forms.ToolStripMenuItem _stall;

    public TrayIcon()
    {
        _icons = Enum.GetValues<TrayState>().ToDictionary(s => s, IconFactory.TrayIcon);
        _stutter = new System.Windows.Forms.ToolStripMenuItem("Mark stutter");
        _stall = new System.Windows.Forms.ToolStripMenuItem("Mark input stall");
        var open = new System.Windows.Forms.ToolStripMenuItem("Open Jankscope");
        var quit = new System.Windows.Forms.ToolStripMenuItem("Quit (stops capturing)");
        open.Click += (_, _) => OpenRequested?.Invoke();
        _stutter.Click += (_, _) => MarkStutterRequested?.Invoke();
        _stall.Click += (_, _) => MarkInputStallRequested?.Invoke();
        quit.Click += (_, _) => QuitRequested?.Invoke();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.AddRange([open, new System.Windows.Forms.ToolStripSeparator(), _stutter, _stall, new System.Windows.Forms.ToolStripSeparator(), quit]);
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _icons[TrayState.Idle],
            Text = "Jankscope",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;

    public event Action? MarkStutterRequested;

    public event Action? MarkInputStallRequested;

    public event Action? QuitRequested;

    public string Tooltip => _icon.Text;

    public TrayState State { get; private set; } = TrayState.Idle;

    public void Update(TrayState state, string tooltip)
    {
        State = state;
        _icon.Icon = _icons[state];
        _icon.Text = tooltip.Length <= MaxTipLength ? tooltip : tooltip[..(MaxTipLength - 1)] + "~";
    }

    /// <summary>Shows each chord in its menu item, or says it is not registered, so the person always knows what works.</summary>
    public void UpdateMenu(string stutterText, string stallText)
    {
        _stutter.Text = stutterText;
        _stall.Text = stallText;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }
    }
}
