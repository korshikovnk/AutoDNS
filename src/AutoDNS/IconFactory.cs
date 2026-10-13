using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AutoDNS;

internal static class IconFactory
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon CreateTrayIcon(Color fill, bool updateAvailable = false)
    {
        using var bitmap = CreateBitmap(32, fill, updateAvailable);
        return ToIcon(bitmap);
    }

    public static void SaveIco(string path, Color fill)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var images = sizes.Select(size => CreateBitmap(size, fill)).ToArray();
        try
        {
            WriteIco(path, images);
        }
        finally
        {
            foreach (var image in images)
                image.Dispose();
        }
    }

    public static Bitmap CreateBitmap(int size, Color fill, bool updateAvailable = false)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.CompositingQuality = CompositingQuality.HighQuality;

        var fontSize = size * 0.84f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        var rect = new RectangleF(0, size * 0.01f, size, size);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var path = new GraphicsPath();
        path.AddString("D", font.FontFamily, (int)FontStyle.Bold, font.Size, rect, format);

        var outlineWidth = Math.Max(1f, size / 18f);
        using var outline = new Pen(Color.FromArgb(200, 18, 18, 18), outlineWidth)
        {
            LineJoin = LineJoin.Round
        };
        g.DrawPath(outline, path);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        if (updateAvailable)
            DrawUpdateBadge(g, size);
        return bitmap;
    }

    internal static int UpdateBadgeOutline(int size) =>
        Math.Max(2, (int)Math.Round(size * 0.06));

    internal static Rectangle UpdateBadgeBounds(int size)
    {
        var diameter = Math.Max(8, (int)Math.Round(size * 0.50));
        return new Rectangle(size - diameter, 0, diameter, diameter);
    }

    private static void DrawUpdateBadge(Graphics graphics, int size)
    {
        var bounds = UpdateBadgeBounds(size);
        var outline = UpdateBadgeOutline(size);
        using var outlineBrush = new SolidBrush(Color.White);
        graphics.FillEllipse(outlineBrush, bounds);
        var inner = Rectangle.Inflate(bounds, -outline, -outline);
        using var brush = new SolidBrush(AppConstants.UpdateBadge);
        graphics.FillEllipse(brush, inner);
    }

    private static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void WriteIco(string path, IReadOnlyList<Bitmap> images)
    {
        var payloads = images.Select(ToPngBytes).ToList();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)images.Count);

        var offset = 6 + (16 * images.Count);
        for (var i = 0; i < images.Count; i++)
        {
            var size = images[i].Width;
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(payloads[i].Length);
            writer.Write(offset);
            offset += payloads[i].Length;
        }

        foreach (var payload in payloads)
            writer.Write(payload);
    }

    private static byte[] ToPngBytes(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
