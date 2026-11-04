using System.Windows.Media.Imaging;

namespace Jankscope.App.Services;

/// <summary>What the notification-area icon says: the only feedback during play besides a sound (no overlay, HC-1).</summary>
public enum TrayState
{
    /// <summary>Not capturing.</summary>
    Idle = 0,

    /// <summary>Capturing and frames are arriving.</summary>
    Capturing = 1,

    /// <summary>Capturing but the program presents nothing right now.</summary>
    Waiting = 2,

    /// <summary>Something needs attention: a hotkey is not working or the capture failed.</summary>
    Attention = 3,
}

/// <summary>
/// Draws the program's icons at run time (a coloured dot) so no icon file and no native icon call is needed. The icon is a PNG wrapped in an ICO header,
/// which Windows has accepted since Vista.
/// </summary>
public static class IconFactory
{
    public const int Size = 32;

    public static byte[] Png(TrayState state)
    {
        using var bitmap = new System.Drawing.Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var ring = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 30, 30, 36));
            using var fill = new System.Drawing.SolidBrush(ColorFor(state));
            graphics.FillEllipse(ring, 1, 1, Size - 3, Size - 3);
            graphics.FillEllipse(fill, 5, 5, Size - 11, Size - 11);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    public static byte[] Ico(TrayState state)
    {
        var png = Png(state);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)0);      // reserved
        writer.Write((short)1);      // type: icon
        writer.Write((short)1);      // one image
        writer.Write((byte)Size);
        writer.Write((byte)Size);
        writer.Write((byte)0);       // no palette
        writer.Write((byte)0);
        writer.Write((short)1);      // planes
        writer.Write((short)32);     // bits per pixel
        writer.Write(png.Length);
        writer.Write(22);            // offset of the image data: 6 + 16
        writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }

    public static System.Drawing.Icon TrayIcon(TrayState state)
    {
        using var stream = new MemoryStream(Ico(state));
        return new System.Drawing.Icon(stream);
    }

    public static BitmapFrame WindowIcon()
    {
        using var stream = new MemoryStream(Png(TrayState.Capturing));
        return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }

    private static System.Drawing.Color ColorFor(TrayState state) => state switch
    {
        TrayState.Capturing => System.Drawing.Color.FromArgb(255, 70, 200, 110),
        TrayState.Waiting => System.Drawing.Color.FromArgb(255, 240, 180, 50),
        TrayState.Attention => System.Drawing.Color.FromArgb(255, 235, 80, 70),
        _ => System.Drawing.Color.FromArgb(255, 150, 155, 165),
    };
}
