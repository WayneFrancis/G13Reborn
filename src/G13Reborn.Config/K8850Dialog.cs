using G13Reborn.Device.Hid;

namespace G13Reborn.Config;

internal sealed class K8850Dialog : Form
{
    private readonly ComboBox _devices = new() { Width = 530, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(570, 0) };
    private readonly NumericUpDown _slot = new() { Minimum = 1, Maximum = 25, Value = 1 };
    private readonly NumericUpDown _layer = new() { Minimum = 1, Maximum = 16, Value = 1 };
    private readonly TextBox _tokens = new() { Width = 300, Text = "04" };
    private readonly Button _upload = new() { Text = "Upload selected binding", AutoSize = true };
    private readonly Button _refresh = new() { Text = "Refresh devices", AutoSize = true };
    private bool _busy;
    private sealed record Choice(K8850Device.Interface Device)
    {
        public override string ToString() => $"K8850 — {Device.OutputLength} bytes — {Device.Path}";
    }

    public K8850Dialog()
    {
        Text = "K8850 Macropad — experimental";
        Size = new Size(650, 540);
        StartPosition = FormStartPosition.CenterParent;
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        Controls.Add(root);
        root.Controls.Add(_devices);
        root.Controls.Add(_refresh);
        var layout = new TableLayoutPanel { ColumnCount = 7, RowCount = 4, AutoSize = true };
        for (int i = 1; i <= 16; i++)
        {
            int slot = i;
            var button = new Button { Text = $"K{i}", Width = 65, Height = 45 };
            button.Click += (_, _) => _slot.Value = slot;
            layout.Controls.Add(button, (i-1)%4, (i-1)/4);
        }
        for (int n = 0; n < 3; n++)
        for (int a = 0; a < 3; a++)
        {
            int slot = 17 + n*3 + a;
            var button = new Button { Text = $"R{n+1} {new[] { "CCW", "Press", "CW" }[a]}", AutoSize = true };
            button.Click += (_, _) => _slot.Value = slot;
            layout.Controls.Add(button, 4+a, n);
        }
        root.Controls.Add(layout);
        var selection = new FlowLayoutPanel { AutoSize = true };
        selection.Controls.AddRange(new Control[] { new Label { Text = "Slot", AutoSize = true }, _slot,
            new Label { Text = "Layer", AutoSize = true }, _layer });
        root.Controls.Add(selection);
        root.Controls.Add(new Label { Text = "Keyboard HID usage tokens (hex, space-separated; 04 = A, F1 = Ctrl)", AutoSize = true });
        root.Controls.Add(_tokens);
        root.Controls.Add(_upload);
        root.Controls.Add(_status);
        _refresh.Click += (_, _) => RefreshDevices();
        _upload.Click += async (_, _) => await UploadAsync();
        _devices.SelectedIndexChanged += (_, _) => UpdateUpload();
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        RefreshDevices();
    }

    private void UpdateUpload() => _upload.Enabled = !_busy &&
        _devices.SelectedItem is Choice c && c.Device.OutputLength is 64 or 65;

    private void RefreshDevices()
    {
        _devices.Items.Clear();
        try
        {
            foreach (var device in K8850Device.Discover()) _devices.Items.Add(new Choice(device));
            if (_devices.Items.Count == 1) _devices.SelectedIndex = 0;
            _status.Text = $"Found {_devices.Items.Count} configuration interface(s). No device data written.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        UpdateUpload();
    }

    private async Task UploadAsync()
    {
        if (_busy || _devices.SelectedItem is not Choice choice) return;
        try
        {
            var tokens = _tokens.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => Convert.ToByte(x, 16)).ToArray();
            var report = K8850Protocol.Keyboard((int)_slot.Value, (int)_layer.Value, tokens);
            if (MessageBox.Show(this, $"Overwrite slot {_slot.Value}, layer {_layer.Value}? Existing assignment cannot be read back.",
                "Confirm hardware write", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _busy = true;
            _refresh.Enabled = _devices.Enabled = false;
            UpdateUpload();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await K8850Device.UploadAsync(choice.Device, report, timeout.Token);
            _status.Text = "Reports sent. Verify the selected physical key in Notepad; no firmware readback is available.";
        }
        catch (Exception ex) { _status.Text = "Upload not confirmed: " + ex.Message; }
        finally
        {
            _busy = false;
            _refresh.Enabled = _devices.Enabled = true;
            UpdateUpload();
        }
    }
}
