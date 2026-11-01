using System.Drawing.Text;
using G13Reborn.Device.Hid;

namespace G13Reborn.Config;

/// <summary>
/// Small, optional 160x43 status screen. Bitmap rendering stays on the UI
/// thread; the resulting report is sent asynchronously once per second.
/// </summary>
internal static class G13LcdStatusRenderer
{
    public static byte[] Render(string profile, string? macro, DateTime localTime)
    {
        using var bitmap = new Bitmap(
            G13HardwareReports.LcdWidth, G13HardwareReports.LcdHeight);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Font font = new("Consolas", 11, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.White);
            graphics.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            Draw(graphics, font, $"G13Reborn  {localTime:HH:mm:ss}", 0);
            Draw(graphics, font, "Profile: " + Shorten(profile, 15), 14);
            Draw(graphics, font,
                "Macro: " + Shorten(macro ?? "(none)", 17), 28);
        }

        return G13HardwareReports.LcdFrame((x, y) =>
        {
            Color pixel = bitmap.GetPixel(x, y);
            return pixel.R + pixel.G + pixel.B < 384;
        });
    }

    private static void Draw(Graphics g, Font font, string text, int y) =>
        g.DrawString(text, font, Brushes.Black, new PointF(0, y));

    private static string Shorten(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
