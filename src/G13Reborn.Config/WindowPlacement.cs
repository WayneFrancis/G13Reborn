using System.Text.Json;

namespace G13Reborn.Config;

internal static class WindowPlacement
{
    private sealed record Placement(int X, int Y, int Width, int Height, bool Maximized);

    public static void Attach(Form form, string name)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "G13Reborn", name + "-window.json");
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Placement>(File.ReadAllText(path)) is { } p
                && p.Width > 0 && p.Height > 0)
            {
                var bounds = new Rectangle(p.X, p.Y, p.Width, p.Height);
                var screen = Screen.AllScreens.FirstOrDefault(s =>
                {
                    var visible = Rectangle.Intersect(s.WorkingArea, new Rectangle(bounds.X, bounds.Y, bounds.Width, 40));
                    return visible.Width >= 100 && visible.Height >= 25;
                });
                if (screen != null)
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Bounds = bounds;
                    if (p.Maximized) form.WindowState = FormWindowState.Maximized;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Placement is optional; retain normal startup behavior.
        }

        form.FormClosed += (_, _) =>
        {
            try
            {
                var bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Placement(bounds.X, bounds.Y,
                    bounds.Width, bounds.Height, form.WindowState == FormWindowState.Maximized)));
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A placement save failure must not prevent shutdown.
            }
        };
    }
}
