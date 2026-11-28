using System.Drawing.Printing;
using System.Drawing.Drawing2D;

namespace G13Reborn.Config;

internal static class K8850PrintLayout
{
    private sealed record Cell(string Slot, string Label, string Binding, string GameActions);
    private sealed record Layer(int Number, string Name, Cell[] Cells);

    public static void Show(Form owner, K8850Profile profile, int currentLayer, MinecraftBindings? minecraft, bool stale)
    {
        // Capture the profile and game snapshot once. The preview will not change
        // while an options.txt refresh or another print pass occurs.
        var layers = Enumerable.Range(1, 3).Select(n => new Layer(n,
            profile.LayerNames.GetValueOrDefault(n, ""),
            Enumerable.Range(1, 25).Select(slot =>
            {
                string key = $"{n}:{slot}";
                string binding = profile.Bindings.GetValueOrDefault(key, "");
                string slotName = slot <= 16 ? $"K{slot}" :
                    $"R{(slot - 17) / 3 + 1} {new[] { "CCW", "Press", "CW" }[(slot - 17) % 3]}";
                string game = minecraft != null && binding.Length != 0
                    ? string.Join("; ", minecraft.Match(binding).Select(minecraft.Describe)) : "";
                return new Cell(slotName, profile.Labels.GetValueOrDefault(key, ""), binding, game);
            }).ToArray())).ToArray();

        using var settings = new Form { Text = "Print macropad layout", Size = new Size(580, 445),
            StartPosition = FormStartPosition.CenterParent };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        settings.Controls.Add(panel);
        var scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        scope.Items.AddRange(new object[] { "All three layers", $"Current layer ({currentLayer})" });
        scope.SelectedIndex = 0;
        var format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
        format.Items.AddRange(new object[] { "Reference sheet (keys and encoders)", "Keycap labels (16 keys per layer)" });
        format.SelectedIndex = 0;
        var names = new CheckBox { Text = "Custom action labels", Checked = true, AutoSize = true };
        var keys = new CheckBox { Text = "Actual key assignments", Checked = true, AutoSize = true };
        var actions = new CheckBox { Text = "Minecraft action descriptions", Checked = minecraft != null && !stale,
            Enabled = minecraft != null, AutoSize = true };
        var width = new NumericUpDown { Minimum = 8, Maximum = 30, DecimalPlaces = 1, Increment = 0.5m, Value = 12, Width = 65 };
        var height = new NumericUpDown { Minimum = 8, Maximum = 30, DecimalPlaces = 1, Increment = 0.5m, Value = 12, Width = 65 };
        var dimensions = new FlowLayoutPanel { AutoSize = true };
        dimensions.Controls.AddRange(new Control[] { new Label { Text = "Label width (mm)", AutoSize = true }, width,
            new Label { Text = "Height (mm)", AutoSize = true }, height });
        var hint = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
        void UpdateFormat()
        {
            dimensions.Enabled = format.SelectedIndex == 1;
            hint.Text = format.SelectedIndex == 1
                ? "Measure your keycap's flat surface first. Print at actual size; check the 20 mm calibration line. Long text may be shortened."
                : "One reference page per selected layer. Long text may be shortened to fit the cell.";
            if (stale) hint.Text += " Minecraft data is from the last successful snapshot.";
        }
        format.SelectedIndexChanged += (_, _) => UpdateFormat();
        panel.Controls.AddRange(new Control[] { scope, format, names, keys, actions, dimensions, hint });
        var preview = new Button { Text = "Print preview...", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        panel.Controls.Add(preview);
        panel.Controls.Add(close);
        settings.CancelButton = close;
        UpdateFormat();

        preview.Click += (_, _) =>
        {
            try
            {
                using var doc = new PrintDocument { DocumentName = "K8850 - " + profile.Name, OriginAtMargins = true };
                if (!doc.PrinterSettings.IsValid)
                    throw new InvalidOperationException("No usable printer selected. Install or select a Windows printer, such as Microsoft Print to PDF.");
                doc.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
                using var printer = new PrintDialog { Document = doc, UseEXDialog = true, AllowSomePages = false,
                    AllowSelection = false, AllowCurrentPage = false };
                if (printer.ShowDialog(settings) != DialogResult.OK) return;
                var selected = scope.SelectedIndex == 0 ? layers : new[] { layers[currentLayer - 1] };
                bool compact = format.SelectedIndex == 1;
                float cellWidth = (float)width.Value * 100f / 25.4f;
                float cellHeight = (float)height.Value * 100f / 25.4f;
                int page = 0;
                doc.BeginPrint += (_, _) => page = 0;
                doc.PrintPage += (_, e) =>
                {
                    var graphics = e.Graphics ?? throw new InvalidOperationException("Printer graphics unavailable.");
                    graphics.PageUnit = GraphicsUnit.Display;
                    var bounds = new RectangleF(0, 0, e.MarginBounds.Width, e.MarginBounds.Height);
                    DrawPage(graphics, bounds, profile.Name, selected[page], compact, cellWidth, cellHeight,
                        names.Checked, keys.Checked, actions.Checked, stale);
                    e.HasMorePages = ++page < selected.Length;
                };
                using var window = new PrintPreviewDialog { Document = doc, Width = 1000, Height = 750 };
                window.ShowDialog(settings);
            }
            catch (Exception ex) { MessageBox.Show(settings, ex.Message, "Print layout"); }
        };
        settings.ShowDialog(owner);
    }

    private static void DrawPage(Graphics g, RectangleF area, string profile, Layer layer, bool compact,
        float cellWidth, float cellHeight, bool names, bool keys, bool actions, bool stale)
    {
        using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 8);
        using var border = new Pen(Color.Black, 0.7f);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.LineLimit };
        string title = $"{profile} | Layer {layer.Number}" + (layer.Name.Length > 0 ? " - " + layer.Name : "");
        g.DrawString(title, titleFont, Brushes.Black, new RectangleF(0, 0, area.Width, 32), format);
        string note = compact ? "Cut along outlines. Verify scale before cutting." :
            "Local profile reference — unknown slots are not hardware readback.";
        if (actions && stale) note += " Minecraft snapshot is stale.";
        g.DrawString(note, smallFont, Brushes.Black, new RectangleF(0, 34, area.Width, 34), format);

        if (compact)
        {
            const float gap = 8f; // Approximately 2 mm.
            if (4 * cellWidth + 3 * gap > area.Width || 4 * cellHeight + 3 * gap + 100 > area.Height)
                throw new InvalidOperationException("The selected label dimensions do not fit this printable page. Choose smaller labels or a larger paper size.");
            for (int i = 0; i < 16; i++)
                DrawCell(g, new RectangleF((i % 4) * (cellWidth + gap), 72 + (i / 4) * (cellHeight + gap),
                    cellWidth, cellHeight), layer.Cells[i], names, keys, actions, true, border);
            float y = 72 + 4 * cellHeight + 3 * gap + 20;
            float scale = 20 * 100f / 25.4f;
            g.DrawLine(border, 0, y, scale, y);
            g.DrawLine(border, 0, y - 4, 0, y + 4);
            g.DrawLine(border, scale, y - 4, scale, y + 4);
            g.DrawString("20 mm", smallFont, Brushes.Black, 0, y + 5);
            return;
        }

        const float header = 72, separator = 30, footer = 28;
        float h = (area.Height - header - separator - footer) / 7;
        if (h < 35 || area.Width < 240)
            throw new InvalidOperationException("Printable page too small for the reference layout.");
        for (int i = 0; i < 16; i++)
            DrawCell(g, new RectangleF((i % 4) * area.Width / 4, header + (i / 4) * h,
                area.Width / 4, h), layer.Cells[i], names, keys, actions, false, border);
        float encoderTop = header + 4 * h + separator;
        g.DrawString("Rotary encoder actions (CCW / Press / CW)", smallFont, Brushes.Black,
            new RectangleF(0, encoderTop - separator + 8, area.Width, separator - 8), format);
        for (int i = 0; i < 9; i++)
            DrawCell(g, new RectangleF((i % 3) * area.Width / 3, encoderTop + (i / 3) * h,
                area.Width / 3, h), layer.Cells[16 + i], names, keys, actions, false, border);
        g.DrawString("Printed from G13Reborn local profile. Long descriptions may be truncated.", smallFont,
            Brushes.Black, new RectangleF(0, area.Height - footer + 6, area.Width, footer - 6), format);
    }

    private static void DrawCell(Graphics g, RectangleF rect, Cell cell, bool names, bool keys, bool actions,
        bool compact, Pen border)
    {
        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        float inset = compact ? 2 : 5;
        var textRect = RectangleF.Inflate(rect, -inset, -inset);
        var lines = new List<string> { cell.Slot };
        if (names && cell.Label.Length > 0) lines.Add(cell.Label);
        if (keys) lines.Add(cell.Binding.Length == 0 ? "Unknown" : cell.Binding);
        if (actions && cell.GameActions.Length > 0 && (!names || cell.GameActions != cell.Label))
            lines.Add(cell.GameActions);
        string text = string.Join("\n", lines);
        using var format = new StringFormat { Alignment = compact ? StringAlignment.Center : StringAlignment.Near,
            LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.LineLimit };
        var state = g.Save();
        try
        {
            g.SetClip(textRect, CombineMode.Intersect);
            float minimum = compact ? 5f : 7f;
            for (float size = compact ? 8f : 11f; size >= minimum; size -= 0.5f)
            {
                using var font = new Font("Segoe UI", size);
                g.MeasureString(text, font, textRect.Size, format, out int fitted, out _);
                if (fitted < text.Length && size > minimum) continue;
                g.DrawString(text, font, Brushes.Black, textRect, format);
                break;
            }
        }
        finally { g.Restore(state); }
    }
}
