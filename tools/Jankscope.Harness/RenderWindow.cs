namespace Jankscope.Harness;

/// <summary>
/// The small window the flip-model swap chain presents to. WinForms creates it, so the harness needs no native window-creation import.
/// By default the window exists (it has a handle, which is all DXGI needs) but is never shown; <c>--window-visible</c> shows it without
/// taking focus. It is a toolbox-style window titled as simulated, so nobody mistakes it for a game.
/// </summary>
internal sealed class RenderWindow : Form
{
    private static int s_live;

    private readonly bool _visible;
    private int _counted = 1;

    public RenderWindow(bool visible, int width, int height)
    {
        _visible = visible;
        Interlocked.Increment(ref s_live);

        Text = "Jankscope synthetic harness - SIMULATED workload";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(40, 40);
        ClientSize = new Size(width, height);
    }

    /// <summary>Windows created and not yet disposed. A test reads this to prove a run (or a failed run) leaves nothing behind.</summary>
    public static int LiveInstances => Volatile.Read(ref s_live);

    protected override bool ShowWithoutActivation => true;

    /// <summary>Creates the native window (and shows it when asked to). Returns the window handle for the swap chain.</summary>
    public IntPtr Open()
    {
        if (_visible)
        {
            Show();
        }

        return Handle;
    }

    /// <summary>Handles pending window messages so a visible window keeps responding. Does nothing for a hidden window.</summary>
    public void Pump()
    {
        if (_visible)
        {
            Application.DoEvents();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _counted, 0) == 1)
        {
            Interlocked.Decrement(ref s_live);
        }

        base.Dispose(disposing);
    }
}
