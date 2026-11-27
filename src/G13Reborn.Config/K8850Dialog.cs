using G13Reborn.Device.Hid;

namespace G13Reborn.Config;

internal sealed class K8850Dialog : Form
{
    private readonly ComboBox _devices = new() { Width = 530, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(570, 0) };
    private readonly NumericUpDown _slot = new() { Minimum = 1, Maximum = 25, Value = 1 };
    private readonly NumericUpDown _layer = new() { Minimum = 1, Maximum = 16, Value = 1 };
    private readonly TextBox _tokens = new() { Width = 300, PlaceholderText = "Unknown / not configured" };
    private readonly Button _upload = new() { Text = "Upload selected binding", AutoSize = true };
    private readonly Button _refresh = new() { Text = "Refresh devices", AutoSize = true };
    private bool _busy, _loading;
    private K8850Profiles _store = new();
    private K8850Profile _profile = new();
    private readonly ComboBox _profiles = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _name = new() { Width = 150, PlaceholderText = "Profile name" };
    private readonly Dictionary<int, Button> _buttons = new();
    private readonly Button _modified = new() { Text = "Upload modified", AutoSize = true };
    private readonly Button _all = new() { Text = "Upload profile", AutoSize = true };
    private string BindingKey => $"{_layer.Value}:{_slot.Value}";
    private string SentKey(string key) => ((Choice?)_devices.SelectedItem)?.Device.Path + "|" + key;
    private bool Changed(string key, string value) => !_store.LastSent.TryGetValue(SentKey(key), out var sent) || sent != value;

    private sealed record Choice(K8850Device.Interface Device)
    {
        public override string ToString() => $"K8850 — {Device.OutputLength} bytes — {Device.Path}";
    }

    public K8850Dialog()
    {
        Text = "K8850 Macropad — experimental";
        Size = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterParent;
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        Controls.Add(root);
        try { _store = K8850Profiles.Load(); }
        catch (Exception ex) { MessageBox.Show("Cannot load profiles: " + ex.Message); throw; }
        var profiles = new FlowLayoutPanel { AutoSize = true };
        profiles.Controls.AddRange(new Control[] { _profiles, _name });
        void Add(string title, Action action)
        {
            var b = new Button { Text = title, AutoSize = true };
            b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } };
            profiles.Controls.Add(b);
        }
        Add("New", () => CreateProfile(false));
        Add("Clone", () => CreateProfile(true));
        Add("Rename", () => { _profile.Name = NewName(); _store.Active = _profile.Name; Save(); ReloadProfiles(); });
        Add("Delete", () => {
            if (_store.Profiles.Count <= 1) throw new InvalidOperationException("Keep at least one profile.");
            if (MessageBox.Show(this, "Delete local profile?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _store.Profiles.Remove(_profile); _store.Active = _store.Profiles[0].Name; Save(); ReloadProfiles();
        });
        Add("Save", Save);
        root.Controls.Add(profiles);
        root.Controls.Add(_devices);
        root.Controls.Add(_refresh);
        var layout = new TableLayoutPanel { ColumnCount = 7, RowCount = 4, AutoSize = true };
        for (int i = 1; i <= 16; i++)
        {
            int slot = i;
            var button = new Button { Text = $"K{i}", Width = 100, Height = 65 };
            _buttons[slot] = button;
            button.Click += (_, _) => _slot.Value = slot;
            layout.Controls.Add(button, (i-1)%4, (i-1)/4);
        }
        for (int n = 0; n < 3; n++)
        for (int a = 0; a < 3; a++)
        {
            int slot = 17 + n*3 + a;
            var button = new Button { Text = $"R{n+1} {new[] { "CCW", "Press", "CW" }[a]}", Width = 100, Height = 65 };
            _buttons[slot] = button;
            button.Click += (_, _) => _slot.Value = slot;
            layout.Controls.Add(button, 4+a, n);
        }
        root.Controls.Add(layout);
        var selection = new FlowLayoutPanel { AutoSize = true };
        selection.Controls.AddRange(new Control[] { new Label { Text = "Slot", AutoSize = true }, _slot,
            new Label { Text = "Layer", AutoSize = true }, _layer });
        root.Controls.Add(selection);
        root.Controls.Add(new Label { Text = "Assignment: Ctrl+C, A, Volume Up, Left Click, Wheel Down, or hex: F1 06", AutoSize = true });
        root.Controls.Add(_tokens);
        var uploads = new FlowLayoutPanel { AutoSize = true };
        uploads.Controls.AddRange(new Control[] { _upload, _modified, _all });
        root.Controls.Add(uploads);
        root.Controls.Add(_status);
        _refresh.Click += (_, _) => RefreshDevices();
        _upload.Click += async (_, _) => await UploadAsync(0);
        _modified.Click += async (_, _) => await UploadAsync(1);
        _all.Click += async (_, _) => await UploadAsync(2);
        _slot.ValueChanged += (_, _) => ShowBinding();
        _layer.ValueChanged += (_, _) => ShowBinding();
        _tokens.TextChanged += (_, _) => {
            if (_loading) return;
            if (string.IsNullOrWhiteSpace(_tokens.Text)) _profile.Bindings.Remove(BindingKey);
            else _profile.Bindings[BindingKey] = _tokens.Text.Trim();
            RefreshLabels();
        };
        _profiles.SelectedIndexChanged += (_, _) => {
            if (_loading || _profiles.SelectedItem is not K8850Profile p) return;
            _profile = p; _store.Active = p.Name; ShowBinding();
        };
        ReloadProfiles();
        _devices.SelectedIndexChanged += (_, _) => RefreshLabels();
        FormClosing += (_, e) => { if (_busy) { e.Cancel = true; return; } try { Save(); } catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, ex.Message); } };
        RefreshDevices();
    }

    private void Save() => _store.Save();
    private string NewName()
    {
        var name = _name.Text.Trim();
        if (name.Length == 0 || _store.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Enter a unique profile name.");
        return name;
    }
    private void CreateProfile(bool clone)
    {
        var p = new K8850Profile { Name = NewName(), Bindings = clone ? new(_profile.Bindings) : new() };
        _store.Profiles.Add(p); _store.Active = p.Name; Save(); ReloadProfiles();
    }
    private void ReloadProfiles()
    {
        _loading = true;
        _profiles.Items.Clear();
        _profiles.DisplayMember = nameof(K8850Profile.Name);
        foreach (var p in _store.Profiles) _profiles.Items.Add(p);
        _profile = _store.Profiles.First(p => p.Name == _store.Active);
        _profiles.SelectedItem = _profile;
        _loading = false;
        ShowBinding();
    }
    private void ShowBinding()
    {
        _loading = true;
        _tokens.Text = _profile.Bindings.GetValueOrDefault(BindingKey, "");
        _loading = false;
        RefreshLabels();
    }
    private void RefreshLabels()
    {
        foreach (var (slot, button) in _buttons)
        {
            var key = $"{_layer.Value}:{slot}";
            var label = slot <= 16 ? $"K{slot}" : $"R{(slot-17)/3+1} {new[] { "CCW", "Press", "CW" }[(slot-17)%3]}";
            button.Text = label + "\n" + (_profile.Bindings.TryGetValue(key, out var value)
                ? value + (Changed(key, value) ? " *" : " [sent]") : "Unknown");
        }
        UpdateUpload();
    }
    private void UpdateUpload()
    {
        bool ready = !_busy && _devices.SelectedItem is Choice c && c.Device.OutputLength is 64 or 65;
        _upload.Enabled = ready && _profile.Bindings.ContainsKey(BindingKey);
        _modified.Enabled = ready && _profile.Bindings.Any(x => Changed(x.Key, x.Value));
        _all.Enabled = ready && _profile.Bindings.Count > 0;
    }

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

    private async Task UploadAsync(int mode)
    {
        if (_busy || _devices.SelectedItem is not Choice choice) return;
        try
        {
            var entries = _profile.Bindings.Where(x => mode == 0 ? x.Key == BindingKey :
                mode == 1 ? Changed(x.Key, x.Value) : true).ToArray();
            var reports = entries.Select(x => {
                var parts = x.Key.Split(':');
                return K8850Binding.Encode(int.Parse(parts[1]), int.Parse(parts[0]), x.Value);
            }).ToArray();
            if (reports.Length == 0) return;
            if (MessageBox.Show(this, $"Overwrite {reports.Length} configured binding(s)? Unknown slots will not be written.",
                "Confirm hardware write", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _busy = true;
            foreach (Control control in Controls) control.Enabled = false;
            for (int i = 0; i < reports.Length; i++)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await K8850Device.UploadAsync(choice.Device, reports[i], timeout.Token);
                _store.LastSent[SentKey(entries[i].Key)] = entries[i].Value;
                Save();
            }
            _status.Text = $"Sent {reports.Length} binding(s). No firmware readback available.";
        }
        catch (Exception ex) { _status.Text = "Upload not confirmed: " + ex.Message; }
        finally
        {
            _busy = false;
            foreach (Control control in Controls) control.Enabled = true;
            RefreshLabels();
        }
    }
}
