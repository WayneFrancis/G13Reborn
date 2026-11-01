using G13Reborn.Device.Hid;

namespace G13Reborn.Config;

/// <summary>
/// Manual, one-shot Logitech G13 hardware diagnostics. Intentionally
/// separate from input forwarding and never runs a background LCD loop.
/// </summary>
internal sealed class G13HardwareDialog : Form
{
    private sealed record InterfaceOption(G13DeviceInfo Device)
    {
        public override string ToString()
        {
            string tail = Device.Path.Length > 42
                ? Device.Path[^42..] : Device.Path;
            return $"IN {Device.InputReportLength}, OUT {Device.OutputReportLength}, " +
                $"FEATURE {Device.FeatureReportLength} — {tail}";
        }
    }

    private readonly ComboBox _feature = new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly ComboBox _lcd = new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly Panel _colourSample = new()
    {
        Width = 44, Height = 28,
        BackColor = Color.FromArgb(0x5A, 0xFF, 0x6E),
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly CheckBox _m1 = new() { Text = "M1", AutoSize = true };
    private readonly CheckBox _m2 = new() { Text = "M2", AutoSize = true };
    private readonly CheckBox _m3 = new() { Text = "M3", AutoSize = true };
    private readonly CheckBox _mr = new() { Text = "MR", AutoSize = true };
    private readonly Label _result = new()
    {
        Dock = DockStyle.Fill,
        Text = "Output commands are sent only when you click Apply / Send.",
        AutoSize = false
    };
    private readonly Button _rgbApply = new() { Text = "Apply RGB", AutoSize = true };
    private readonly Button _ledApply = new() { Text = "Apply M LEDs", AutoSize = true };
    private readonly Button _readLeds = new() { Text = "Read LEDs", AutoSize = true };
    private readonly Button _testLcd = new() { Text = "Send test pattern", AutoSize = true };
    private readonly Button _clearLcd = new() { Text = "Clear LCD", AutoSize = true };

    public G13HardwareDialog()
    {
        Text = "G13Reborn — LCD and backlight diagnostics";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 510);
        MinimumSize = new Size(680, 460);
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1, RowCount = 8
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var explanation = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Experimental G13 hardware output. Select the HID collection " +
                "with a FEATURE report for the RGB/M LEDs, or the collection with a " +
                "992-byte OUT report for the 160×43 LCD.\r\n" +
                "Stop forwarding and fully quit LGS from its tray icon before testing. " +
                "LGS can overwrite our LCD/LED state when M1/M2/M3 is pressed.\r\n" +
                "Checkboxes stage an LED mask; click Apply M LEDs to send it. " +
                "Read LEDs queries the device without changing its state."
        };
        root.Controls.Add(explanation, 0, 0);

        var featureHeading = new FlowLayoutPanel { Dock = DockStyle.Fill };
        featureHeading.Controls.Add(new Label
        {
            Text = "RGB and M-key LED HID interface", AutoSize = true,
            Padding = new Padding(0, 7, 4, 0)
        });
        var refresh = new Button { Text = "Refresh interfaces", AutoSize = true };
        featureHeading.Controls.Add(refresh);
        root.Controls.Add(featureHeading, 0, 1);
        root.Controls.Add(_feature, 0, 2);

        var ledRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var choose = new Button { Text = "Choose RGB...", AutoSize = true };
        ledRow.Controls.Add(choose);
        ledRow.Controls.Add(_colourSample);
        ledRow.Controls.Add(_rgbApply);
        ledRow.Controls.Add(new Label
        {
            Text = "   M-key LEDs:", AutoSize = true,
            Padding = new Padding(0, 7, 0, 0)
        });
        ledRow.Controls.AddRange(new Control[]
        {
            _m1, _m2, _m3, _mr, _ledApply, _readLeds
        });
        root.Controls.Add(ledRow, 0, 3);

        root.Controls.Add(new Label
        {
            Text = "LCD output interface (992 bytes)", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 4);
        root.Controls.Add(_lcd, 0, 5);

        var lcdButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        lcdButtons.Controls.AddRange(new Control[] { _testLcd, _clearLcd });
        root.Controls.Add(lcdButtons, 0, 6);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        var close = new Button
        {
            Text = "Close", Dock = DockStyle.Top,
            DialogResult = DialogResult.Cancel
        };
        bottom.Controls.Add(_result, 0, 0);
        bottom.Controls.Add(close, 1, 0);
        root.Controls.Add(bottom, 0, 7);
        CancelButton = close;

        refresh.Click += (_, _) => RefreshInterfaces();
        choose.Click += (_, _) =>
        {
            using var picker = new ColorDialog
            {
                Color = _colourSample.BackColor,
                FullOpen = true
            };
            if (picker.ShowDialog(this) == DialogResult.OK)
                _colourSample.BackColor = picker.Color;
        };
        _rgbApply.Click += (_, _) => ApplyRgb();
        _ledApply.Click += (_, _) => ApplyLeds();
        _readLeds.Click += (_, _) => ReadLeds();
        _testLcd.Click += async (_, _) =>
            await SendLcdAsync(G13HardwareReports.TestPattern(), "Test pattern sent.");
        _clearLcd.Click += async (_, _) =>
            await SendLcdAsync(G13HardwareReports.BlankLcd(), "LCD cleared.");

        RefreshInterfaces();
    }

    private void RefreshInterfaces()
    {
        try
        {
            string? featurePath = (_feature.SelectedItem as InterfaceOption)?.Device.Path;
            string? lcdPath = (_lcd.SelectedItem as InterfaceOption)?.Device.Path;
            var devices = G13DeviceDiscovery.Enumerate();

            _feature.Items.Clear();
            _lcd.Items.Clear();
            foreach (var device in devices)
            {
                if (device.FeatureReportLength >= G13HardwareReports.FeatureLength)
                    _feature.Items.Add(new InterfaceOption(device));
                if (device.OutputReportLength == G13HardwareReports.LcdReportLength)
                    _lcd.Items.Add(new InterfaceOption(device));
            }
            SelectInterface(_feature, featurePath);
            SelectInterface(_lcd, lcdPath);
            _rgbApply.Enabled = _ledApply.Enabled = _readLeds.Enabled =
                _feature.SelectedItem is not null;
            _testLcd.Enabled = _clearLcd.Enabled = _lcd.SelectedItem is not null;
            _result.Text = $"Found {_feature.Items.Count} feature collection(s) and " +
                $"{_lcd.Items.Count} LCD collection(s).";
        }
        catch (Exception ex) { ShowError("HID enumeration failed", ex); }
    }

    private static void SelectInterface(ComboBox list, string? preferredPath)
    {
        int index = 0;
        for (int i = 0; i < list.Items.Count; i++)
            if ((list.Items[i] as InterfaceOption)?.Device.Path == preferredPath)
                index = i;
        if (list.Items.Count > 0) list.SelectedIndex = index;
    }

    private void ApplyRgb()
    {
        if (_feature.SelectedItem is not InterfaceOption output) return;
        try
        {
            Color c = _colourSample.BackColor;
            G13OutputDevice.SetRgb(output.Device, c.R, c.G, c.B);
            _result.Text = $"Sent RGB #{c.R:X2}{c.G:X2}{c.B:X2}.";
        }
        catch (Exception ex) { ShowError("RGB backlight failed", ex); }
    }

    private void ApplyLeds()
    {
        if (_feature.SelectedItem is not InterfaceOption output) return;
        byte wanted = G13HardwareReports.MacroLeds(
            _m1.Checked, _m2.Checked, _m3.Checked, _mr.Checked)[1];
        try
        {
            G13OutputDevice.SetMacroLeds(output.Device,
                _m1.Checked, _m2.Checked, _m3.Checked, _mr.Checked);
        }
        catch (Exception ex)
        {
            ShowError("Macro LEDs failed", ex);
            return;
        }

        try
        {
            byte actual = G13OutputDevice.ReadMacroLedMask(output.Device);
            _result.Text = actual == wanted
                ? $"Wrote M LEDs 0x{wanted:X2}; read back 0x{actual:X2} (match)."
                : $"Wrote M LEDs 0x{wanted:X2}; read back 0x{actual:X2} " +
                  "(different — another program may be changing them).";
        }
        catch (Exception ex)
        {
            _result.Text = $"Wrote M LEDs 0x{wanted:X2}, but device readback " +
                $"is unavailable: {ex.Message}";
        }
    }

    private void ReadLeds()
    {
        if (_feature.SelectedItem is not InterfaceOption output) return;
        try
        {
            byte mask = G13OutputDevice.ReadMacroLedMask(output.Device);
            _m1.Checked = (mask & 0x01) != 0;
            _m2.Checked = (mask & 0x02) != 0;
            _m3.Checked = (mask & 0x04) != 0;
            _mr.Checked = (mask & 0x08) != 0;
            _result.Text = $"Device LED mask: 0x{mask:X2}. " +
                "Checkboxes updated to the reported state; no command sent.";
        }
        catch (Exception ex) { ShowError("Read M LEDs failed", ex); }
    }

    private async Task SendLcdAsync(byte[] frame, string success)
    {
        if (_lcd.SelectedItem is not InterfaceOption output) return;
        _testLcd.Enabled = _clearLcd.Enabled = false;
        try
        {
            await G13OutputDevice.SendLcdFrameAsync(output.Device, frame);
            if (!IsDisposed) _result.Text = success;
        }
        catch (Exception ex) { if (!IsDisposed) ShowError("LCD output failed", ex); }
        finally
        {
            if (!IsDisposed)
                _testLcd.Enabled = _clearLcd.Enabled = true;
        }
    }

    private void ShowError(string title, Exception ex)
    {
        _result.Text = $"{title}: {ex.Message}";
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
