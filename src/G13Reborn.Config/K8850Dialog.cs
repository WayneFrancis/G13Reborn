using G13Reborn.Device.Hid;

namespace G13Reborn.Config;

internal sealed class K8850Dialog : Form
{
    private readonly ComboBox _devices = new() { Width = 530, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(570, 0) };
    private readonly NumericUpDown _slot = new() { Minimum = 1, Maximum = 25, Value = 1 };
    private readonly NumericUpDown _layer = new() { Minimum = 1, Maximum = 3, Value = 1 };
    private readonly TextBox _layerName = new() { Width = 190, PlaceholderText = "Optional layer name", MaxLength = 60 };
    private readonly TextBox _actionLabel = new() { Width = 240, PlaceholderText = "Optional action label", MaxLength = 100 };
    private readonly ToolTip _tooltips = new() { AutoPopDelay = 30000 };
    private MinecraftBindings? _minecraftBindings;
    private (K8850Profile Profile, string Key, string Binding, string Label)? _copied;
    private readonly Button _copy = new() { Text = "Copy assignment", AutoSize = true };
    private readonly Button _paste = new() { Text = "Paste assignment", AutoSize = true };
    private readonly Button _swap = new() { Text = "Swap with copied slot", AutoSize = true };
    private readonly ComboBox _tokens = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _category = new() { Width = 175, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FlowLayoutPanel _modifiers = new() { AutoSize = true };
    private readonly CheckBox[] _modifierChecks = new[] { "Ctrl", "Shift", "Alt", "Win", "RCtrl", "RShift", "RAlt", "RWin" }
        .Select(name => new CheckBox { Text = name, AutoSize = true }).ToArray();
    private readonly Button _capture = new() { Text = "Capture shortcut...", AutoSize = true };
    private static readonly string[] Media = { "Volume Up", "Volume Down", "Mute", "Play/Pause", "Next Track", "Previous Track", "Stop Media" };
    private static readonly string[] MouseActions = { "Left Click", "Right Click", "Middle Click", "Wheel Up", "Wheel Down" };
    private static readonly string[] Keyboard = Enumerable.Range(0, 26).Select(i => ((char)('A' + i)).ToString())
        .Concat(Enumerable.Range(0, 10).Select(i => i.ToString()))
        .Concat(Enumerable.Range(1, 24).Select(i => $"F{i}"))
        .Concat(new[] { "Enter", "Escape", "Backspace", "Tab", "Space", "Delete", "Home", "End", "PageUp", "PageDown", "Left", "Right", "Up", "Down", "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+A", "Ctrl+S" }).ToArray();
    private static readonly string[] ExtendedKeyboard = Enumerable.Range(13, 12).Select(i => $"F{i}")
        .Concat(Enumerable.Range(0, 10).Select(i => $"Numpad {i}"))
        .Concat(new[] { "Numpad Divide", "Numpad Multiply", "Numpad Subtract", "Numpad Add",
            "Numpad Enter", "Numpad Decimal", "Numpad Equals", "Num Lock", "Print Screen",
            "Scroll Lock", "Pause", "Insert", "Non-US Backslash", "Application/Menu",
            "Execute", "Help", "Menu", "Select", "Stop", "Again", "Undo", "Cut", "Copy", "Paste", "Find" })
        .Concat(Enumerable.Range(1, 9).Select(i => $"International {i}"))
        .Concat(Enumerable.Range(1, 9).Select(i => $"Language {i}")).ToArray();
    private const string GamingCategory = "Gaming / Extended keys";
    private static readonly string[] GamingKeys = Enumerable.Range(13, 12).Select(i => $"F{i}")
        .Concat(Enumerable.Range(1, 6).Select(i => $"International {i}"))
        .Concat(new[] { "Language 3", "Language 4" }).ToArray();
    private static readonly string[] GamingLabels = GamingKeys.Take(12)
        .Concat(GamingKeys.Skip(12).Zip(new[] { 115, 112, 125, 121, 123, 92, 120, 119 },
            (key, scan) => $"{key} (scancode.{scan})")).ToArray();
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
        WindowPlacement.Attach(this, "k8850");
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
        Add("Copy layer...", CopyLayer);
        Add("Validate profile...", ValidateProfile);
        Add("Export profile...", ExportProfile);
        Add("Import profile...", ImportProfile);
        Add("Generate gaming profile...", GenerateGamingProfile);
        profiles.MaximumSize = new Size(850, 0);
        root.Controls.Add(profiles);
        root.Controls.Add(_devices);
        root.Controls.Add(_refresh);
        var layout = new TableLayoutPanel { ColumnCount = 7, RowCount = 4, AutoSize = true };
        for (int i = 1; i <= 16; i++)
        {
            int slot = i;
            var button = new Button { Text = $"K{i}", Width = 100, Height = 83 };
            _buttons[slot] = button;
            button.Click += (_, _) => _slot.Value = slot;
            layout.Controls.Add(button, (i-1)%4, (i-1)/4);
        }
        for (int n = 0; n < 3; n++)
        for (int a = 0; a < 3; a++)
        {
            int slot = 17 + n*3 + a;
            var button = new Button { Text = $"R{n+1} {new[] { "CCW", "Press", "CW" }[a]}", Width = 100, Height = 83 };
            _buttons[slot] = button;
            button.Click += (_, _) => _slot.Value = slot;
            layout.Controls.Add(button, 4+a, n);
        }
        root.Controls.Add(layout);
        var selection = new FlowLayoutPanel { AutoSize = true };
        selection.Controls.AddRange(new Control[] { new Label { Text = "Slot", AutoSize = true }, _slot,
            new Label { Text = "Edit layer", AutoSize = true }, _layer });
        var layerHelp = new Button { Text = "Layer help / test...", AutoSize = true };
        layerHelp.Click += (_, _) => ShowLayerHelp();
        selection.Controls.AddRange(new Control[] { _layerName, layerHelp });
        root.Controls.Add(selection);
        root.Controls.Add(new Label { Text = "Assignment (select an action or capture a keyboard shortcut)", AutoSize = true });
        var editor = new FlowLayoutPanel { AutoSize = true };
        _category.Items.AddRange(new object[] { "Keyboard", GamingCategory, "Extended keyboard", "Multimedia", "Mouse", "Advanced / hex" });
        editor.Controls.AddRange(new Control[] { _category, _tokens, _capture });
        var forget = new Button { Text = "Forget local assignment", AutoSize = true };
        forget.Click += (_, _) => { SetLocalAssignment(_profile, BindingKey, null, null); ShowBinding(); };
        editor.Controls.Add(forget);
        root.Controls.Add(editor);
        _modifiers.Controls.Add(new Label { Text = "Modifiers:", AutoSize = true });
        foreach (var check in _modifierChecks)
        {
            _modifiers.Controls.Add(check);
            check.CheckedChanged += (_, _) => { if (!_loading) StoreEditorBinding(); };
        }
        root.Controls.Add(_modifiers);
        var metadata = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(850, 0) };
        metadata.Controls.AddRange(new Control[] { new Label { Text = "Action label", AutoSize = true },
            _actionLabel, _copy, _paste, _swap });
        root.Controls.Add(metadata);
        var minecraft = new FlowLayoutPanel { AutoSize = true };
        void AddMinecraft(string title, Action action)
        {
            var button = new Button { Text = title, AutoSize = true };
            button.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } };
            minecraft.Controls.Add(button);
        }
        AddMinecraft("Minecraft options...", SelectMinecraftOptions);
        AddMinecraft("Refresh Minecraft bindings", RefreshMinecraftBindings);
        AddMinecraft("Clear Minecraft link", () => {
            _store.MinecraftOptionsPath = null; _minecraftBindings = null; Save(); RefreshLabels();
            _status.Text = "Minecraft keybinding link cleared.";
        });
        root.Controls.Add(minecraft);
        _layerName.TextChanged += (_, _) => {
            if (_loading) return;
            int layer = (int)_layer.Value;
            if (string.IsNullOrWhiteSpace(_layerName.Text)) _profile.LayerNames.Remove(layer);
            else _profile.LayerNames[layer] = _layerName.Text.Trim();
        };
        _actionLabel.TextChanged += (_, _) => {
            if (_loading) return;
            if (string.IsNullOrWhiteSpace(_actionLabel.Text)) _profile.Labels.Remove(BindingKey);
            else _profile.Labels[BindingKey] = _actionLabel.Text.Trim();
            RefreshLabels();
        };
        _copy.Click += (_, _) => CopyAssignment();
        _paste.Click += (_, _) => PasteAssignment(false);
        _swap.Click += (_, _) => PasteAssignment(true);
        Disposed += (_, _) => _tooltips.Dispose();
        _category.SelectionChangeCommitted += (_, _) => {
            _loading = true;
            ConfigureEditor(_category.Text, "");
            _loading = false;
            // Browsing categories does not erase the existing binding.
        };
        _capture.Click += (_, _) => CaptureShortcut();
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
            if (_loading || _category.Text == GamingCategory) return;
            if (IsKeyboardCategory(_category.Text))
            {
                StoreEditorBinding();
                return;
            }
            if (string.IsNullOrWhiteSpace(_tokens.Text)) return;
            else
            {
                var value = _tokens.Text.Trim();
                if (_category.Text == "Advanced / hex" && !value.StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
                    value = "hex: " + value;
                _profile.Bindings[BindingKey] = value;
            }
            RefreshLabels();
        };
        _tokens.SelectionChangeCommitted += (_, _) => {
            if (_loading || _tokens.SelectedItem is not string value) return;
            if (_category.Text == GamingCategory)
            {
                int index = Array.IndexOf(GamingLabels, value);
                if (index < 0) return;
                value = GamingKeys[index];
            }
            if (IsKeyboardCategory(_category.Text))
            {
                StoreEditorBinding();
                return;
            }
            _profile.Bindings[BindingKey] = value;
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
        if (!string.IsNullOrWhiteSpace(_store.MinecraftOptionsPath))
        {
            try { RefreshMinecraftBindings(); }
            catch (Exception ex) { _status.Text = "Minecraft options unavailable: " + ex.Message; }
        }
    }

    private void SelectMinecraftOptions()
    {
        using var dialog = new OpenFileDialog { Filter = "Minecraft options (options.txt)|options.txt|Text files (*.txt)|*.txt",
            FileName = "options.txt", CheckFileExists = true };
        if (!string.IsNullOrWhiteSpace(_store.MinecraftOptionsPath))
            dialog.InitialDirectory = Path.GetDirectoryName(_store.MinecraftOptionsPath);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var bindings = MinecraftBindings.Load(dialog.FileName);
        _store.MinecraftOptionsPath = dialog.FileName;
        _minecraftBindings = bindings;
        Save();
        RefreshLabels();
        _status.Text = $"Loaded {bindings.Count} saved Minecraft bindings (read-only). Hover a configured action.";
    }

    private void RefreshMinecraftBindings()
    {
        if (string.IsNullOrWhiteSpace(_store.MinecraftOptionsPath))
        {
            SelectMinecraftOptions();
            return;
        }
        _minecraftBindings = null;
        RefreshLabels();
        _minecraftBindings = MinecraftBindings.Load(_store.MinecraftOptionsPath);
        RefreshLabels();
        _status.Text = $"Loaded {_minecraftBindings.Count} saved Minecraft bindings (read-only). Hover a configured action.";
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
        var p = new K8850Profile { Name = NewName(), Bindings = clone ? new(_profile.Bindings) : new(),
            LayerNames = clone ? new(_profile.LayerNames) : new(), Labels = clone ? new(_profile.Labels) : new() };
        _store.Profiles.Add(p); _store.Active = p.Name; Save(); ReloadProfiles();
    }
    private void CopyLayer()
    {
        using var dialog = new Form { Text = "Copy hardware layer", Size = new Size(460, 250),
            StartPosition = FormStartPosition.CenterParent };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12) };
        dialog.Controls.Add(panel);
        int source = (int)_layer.Value;
        var destination = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
        var targets = Enumerable.Range(1, 3).Where(n => n != source).ToArray();
        foreach (int n in targets) destination.Items.Add($"Layer {n}: {_profile.LayerNames.GetValueOrDefault(n, "")}");
        destination.SelectedIndex = 0;
        panel.Controls.Add(new Label { Text = $"Copy layer {source} to:", AutoSize = true });
        panel.Controls.Add(destination);
        var replace = new CheckBox { Text = "Replace existing local destination assignments", AutoSize = true };
        var copyName = new CheckBox { Text = "Copy layer name", AutoSize = true };
        panel.Controls.Add(replace);
        panel.Controls.Add(copyName);
        var ok = new Button { Text = "Copy locally", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        panel.Controls.Add(ok);
        panel.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        int target = targets[destination.SelectedIndex];
        var entries = Enumerable.Range(1, 25).Select(slot => (Slot: slot, Key: $"{source}:{slot}"))
            .Where(x => _profile.Bindings.ContainsKey(x.Key))
            .Select(x => (x.Slot, Binding: _profile.Bindings[x.Key], Label: _profile.Labels.GetValueOrDefault(x.Key, "")))
            .Where(x => replace.Checked || !_profile.Bindings.ContainsKey($"{target}:{x.Slot}")).ToArray();
        foreach (var entry in entries) K8850Binding.Encode(entry.Slot, target, entry.Binding);
        if (replace.Checked && entries.Any(x => _profile.Bindings.ContainsKey($"{target}:{x.Slot}")) &&
            MessageBox.Show(this, $"Replace overlapping assignments on layer {target}? Unknown source slots will not erase destination slots.",
                "Confirm layer copy", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        foreach (var entry in entries)
            SetLocalAssignment(_profile, $"{target}:{entry.Slot}", entry.Binding, entry.Label);
        if (copyName.Checked)
        {
            if (_profile.LayerNames.TryGetValue(source, out var name)) _profile.LayerNames[target] = name;
            else _profile.LayerNames.Remove(target);
        }
        _layer.Value = target;
        ShowBinding();
        _status.Text = $"Copied {entries.Length} local assignments from layer {source} to {target}. No hardware written.";
    }

    private void GenerateGamingProfile()
    {
        using var dialog = new Form { Text = "Generate gaming profile", Size = new Size(530, 430),
            StartPosition = FormStartPosition.CenterParent };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        dialog.Controls.Add(panel);
        var name = new TextBox { Width = 300, Text = string.IsNullOrWhiteSpace(_name.Text) ? "Gaming" : _name.Text.Trim() };
        var layer = new NumericUpDown { Minimum = 1, Maximum = 3, Value = _layer.Value };
        panel.Controls.Add(new Label { Text = "New profile name", AutoSize = true });
        panel.Controls.Add(name);
        panel.Controls.Add(new Label { Text = "Target layer", AutoSize = true });
        panel.Controls.Add(layer);
        panel.Controls.Add(new Label { Text = "Choose one optional modifier for five additional encoder actions:", AutoSize = true });
        var choices = new[] { "Ctrl", "Shift", "Alt", "Win", "RCtrl", "RShift", "RAlt", "RWin" }
            .Select(m => new CheckBox { Text = m, AutoSize = true }).ToArray();
        var row = new FlowLayoutPanel { Width = 460, AutoSize = true };
        row.Controls.AddRange(choices);
        panel.Controls.Add(row);
        var preview = new Label { AutoSize = true, MaximumSize = new Size(460, 0) };
        panel.Controls.Add(preview);
        string? Modifier() => choices.FirstOrDefault(c => c.Checked)?.Text;
        void Preview() => preview.Text = "K1–K12: F13–F24\nK13–K16: International 1–4\\n" +
            "R1 CCW / Press / CW: International 5 / 6 / Language 3\nR2 CCW: Language 4\\n" +
            (Modifier() is string m ? $"Remaining five actions: {m}+F13 through {m}+F17 (game chord support not guaranteed)." :
                "Remaining five actions: unknown / not written.") +
            "\nCreates a new local profile only. Existing profiles and hardware are unchanged.";
        foreach (var choice in choices) choice.CheckedChanged += (_, _) => {
            if (choice.Checked)
                foreach (var other in choices.Where(c => c != choice)) other.Checked = false;
            Preview();
        };
        Preview();
        var create = new Button { Text = "Create local profile", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        panel.Controls.Add(create);
        panel.Controls.Add(cancel);
        dialog.CancelButton = cancel;
        create.Click += (_, _) =>
        {
            string title = name.Text.Trim();
            if (title.Length == 0 || _store.Profiles.Any(p => p.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(dialog, "Enter a unique profile name.");
                return;
            }
            dialog.DialogResult = DialogResult.OK;
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var profile = new K8850Profile { Name = name.Text.Trim() };
        int targetLayer = (int)layer.Value;
        for (int i = 0; i < GamingKeys.Length; i++)
            profile.Bindings[$"{targetLayer}:{i + 1}"] = GamingKeys[i];
        if (Modifier() is string modifier)
            for (int i = 0; i < 5; i++)
                profile.Bindings[$"{targetLayer}:{21 + i}"] = $"{modifier}+F{13 + i}";
        foreach (var entry in profile.Bindings)
            K8850Binding.Encode(int.Parse(entry.Key.Split(':')[1]), targetLayer, entry.Value);
        _store.Profiles.Add(profile);
        _store.Active = profile.Name;
        Save();
        ReloadProfiles();
        _layer.Value = targetLayer;
        _status.Text = $"Created {profile.Bindings.Count} local gaming assignments. Review before uploading.";
    }

    private void ShowLayerHelp()
    {
        MessageBox.Show(this,
            "This 4x4/3-knob device has three usable hardware layers (75 actions).\r\n\r\n" +
            "Edit layer selects the programming target. Use the physical layer button to cycle the active hardware layer. " +
            "The application cannot currently read or switch the active layer.\r\n\r\n" +
            "Older local profiles may contain experimental layers above 3. These are preserved but cannot be uploaded. " +
            "RGB configuration and the additional hardware switch remain under investigation.",
            "Hardware layers", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExportProfile()
    {
        using var dialog = new SaveFileDialog { Filter = "K8850 profile (*.json)|*.json", DefaultExt = "json",
            AddExtension = true, FileName = "k8850-profile.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllText(dialog.FileName, System.Text.Json.JsonSerializer.Serialize(_profile,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        _status.Text = "Exported local profile.";
    }

    private void ImportProfile()
    {
        using var dialog = new OpenFileDialog { Filter = "K8850 profile (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var profile = System.Text.Json.JsonSerializer.Deserialize<K8850Profile>(File.ReadAllText(dialog.FileName))
            ?? throw new InvalidDataException("Invalid profile file.");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Bindings == null)
            throw new InvalidDataException("Profile name or bindings missing.");
        foreach (var entry in profile.Bindings)
        {
            var parts = entry.Key.Split(':');
            if (parts.Length != 2) throw new FormatException("Invalid layer:slot identifier.");
            int layer = int.Parse(parts[0]), slot = int.Parse(parts[1]);
            if (entry.Key != $"{layer}:{slot}") throw new FormatException("Noncanonical layer:slot identifier.");
            K8850Binding.Encode(slot, layer, entry.Value);
        }
        profile.LayerNames ??= new();
        profile.Labels ??= new();
        string original = profile.Name.Trim();
        profile.Name = original;
        for (int i = 2; _store.Profiles.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)); i++)
            profile.Name = $"{original} ({i})";
        if (MessageBox.Show(this, $"Import {profile.Bindings.Count} assignments as '{profile.Name}'? No hardware will be written.",
            "Import profile", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _store.Profiles.Add(profile);
        _store.Active = profile.Name;
        Save();
        ReloadProfiles();
        _status.Text = "Imported local profile. Review before uploading.";
    }

    private void ValidateProfile()
    {
        var issues = new List<string>();
        var packets = new Dictionary<string, List<string>>();
        var gaming = new Dictionary<string, List<string>>();
        foreach (var entry in _profile.Bindings)
        {
            try
            {
                var parts = entry.Key.Split(':');
                if (parts.Length != 2) throw new FormatException("Invalid layer:slot identifier.");
                int layer = int.Parse(parts[0]), slot = int.Parse(parts[1]);
                var report = K8850Binding.Encode(slot, layer, entry.Value);
                string label = $"Layer {layer}, slot {slot}: {entry.Value}";
                if (layer > 3) issues.Add("Unsupported hardware layer: " + label);
                report[2] = 0; // Ignore slot, but keep layer and action bytes.
                string signature = Convert.ToHexString(report);
                if (!packets.TryGetValue(signature, out var matches))
                    packets[signature] = matches = new();
                matches.Add(label);
                // Observed Minecraft aliases, not general Windows equivalence.
                if (report[4] == 1 && report[6] == 1 && report[9] is 0x73 or 0x94)
                {
                    string alias = $"{layer}:F24";
                    if (!gaming.TryGetValue(alias, out var aliases)) gaming[alias] = aliases = new();
                    aliases.Add(label);
                }
                if (report[4] == 2 && report[9] is 0xb5 or 0xb6 && report[12] == 0)
                {
                    string alias = $"{layer}:MediaTrack";
                    if (!gaming.TryGetValue(alias, out var aliases)) gaming[alias] = aliases = new();
                    aliases.Add(label);
                }
            }
            catch (Exception ex) { issues.Add($"Invalid {entry.Key}: {entry.Value} — {ex.Message}"); }
        }
        foreach (var matches in packets.Values.Where(v => v.Count > 1))
            issues.Add("Duplicate encoded assignment: " + string.Join("; ", matches));
        foreach (var matches in gaming.Values.Where(v => v.Count > 1))
            issues.Add("Possible Minecraft input collision: " + string.Join("; ", matches));
        using var dialog = new Form { Text = "Profile validation", Size = new Size(720, 460),
            StartPosition = FormStartPosition.CenterParent };
        var output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, WordWrap = true,
            Text = $"Profile: {_profile.Name}\r\nConfigured assignments: {_profile.Bindings.Count}\r\n" +
                (issues.Count == 0 ? "No encoding errors or known duplicate assignments found." :
                    string.Join("\r\n\r\n", issues)) +
                "\r\n\r\nValidation is local only. Unknown hardware slots and game-specific modifier conflicts cannot be checked." };
        dialog.Controls.Add(output);
        dialog.ShowDialog(this);
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
        _layerName.Text = _profile.LayerNames.GetValueOrDefault((int)_layer.Value, "");
        _actionLabel.Text = _profile.Labels.GetValueOrDefault(BindingKey, "");
        var value = _profile.Bindings.GetValueOrDefault(BindingKey, "");
        var baseKey = SplitModifiers(value).Key;
        var category = Media.Contains(value, StringComparer.OrdinalIgnoreCase) ? "Multimedia" :
            MouseActions.Contains(value, StringComparer.OrdinalIgnoreCase) ? "Mouse" :
            value.StartsWith("hex:", StringComparison.OrdinalIgnoreCase) ? "Advanced / hex" :
            GamingKeys.Contains(baseKey, StringComparer.OrdinalIgnoreCase) ? GamingCategory :
            ExtendedKeyboard.Contains(baseKey, StringComparer.OrdinalIgnoreCase) ? "Extended keyboard" : "Keyboard";
        ConfigureEditor(category, value);
        _loading = false;
        RefreshLabels();
    }
    private static bool IsKeyboardCategory(string category) =>
        category is "Keyboard" or "Extended keyboard" or GamingCategory;

    private static (string Key, string[] Modifiers) SplitModifiers(string value)
    {
        if (value.Contains(',')) return (value, Array.Empty<string>());
        var parts = value.Split('+', StringSplitOptions.TrimEntries);
        var known = new[] { "Ctrl", "Shift", "Alt", "Win", "RCtrl", "RShift", "RAlt", "RWin" };
        if (parts.Length > 1 && parts.Take(parts.Length - 1).All(p => known.Contains(p, StringComparer.OrdinalIgnoreCase)))
            return (parts[^1], parts[..^1]);
        return (value, Array.Empty<string>());
    }

    private void StoreEditorBinding()
    {
        if (!IsKeyboardCategory(_category.Text)) return;
        string key = _tokens.Text.Trim();
        if (_category.Text == GamingCategory)
        {
            int index = _tokens.SelectedIndex;
            if (index < 0) return;
            key = GamingKeys[index];
        }
        if (key.Length == 0) return;
        var modifiers = _modifierChecks.Where(c => c.Checked).Select(c => c.Text).ToArray();
        if (modifiers.Length > 0 && (key.Contains('+') || key.Contains(',')))
        {
            _status.Text = "Use modifier checkboxes with a single key, or clear them to enter a shortcut or sequence.";
            return;
        }
        _profile.Bindings[BindingKey] = string.Join("+", modifiers.Append(key));
        RefreshLabels();
    }

    private void ConfigureEditor(string category, string value)
    {
        _category.SelectedItem = category;
        bool keyboard = IsKeyboardCategory(category);
        var split = keyboard ? SplitModifiers(value) : (Key: value, Modifiers: Array.Empty<string>());
        _modifiers.Enabled = keyboard;
        foreach (var check in _modifierChecks)
            check.Checked = split.Modifiers.Contains(check.Text, StringComparer.OrdinalIgnoreCase);
        value = split.Key;
        _tokens.Items.Clear();
        bool fixedList = category is "Multimedia" or "Mouse" or GamingCategory;
        _tokens.DropDownStyle = fixedList ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown;
        _tokens.Items.AddRange(category switch {
            GamingCategory => GamingLabels, "Multimedia" => Media, "Mouse" => MouseActions, "Keyboard" => Keyboard, "Extended keyboard" => ExtendedKeyboard, _ => Array.Empty<string>()
        });
        if (fixedList)
            _tokens.SelectedIndex = Array.FindIndex(category == GamingCategory ? GamingKeys : category == "Multimedia" ? Media : MouseActions,
                x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        else _tokens.Text = category == "Advanced / hex" && value.StartsWith("hex:", StringComparison.OrdinalIgnoreCase)
            ? value[4..].Trim() : value;
        _capture.Enabled = category is "Keyboard" or "Extended keyboard";
    }

    private void CaptureShortcut()
    {
        using var dialog = new Form { Text = "Capture keyboard shortcut", Size = new Size(420, 150),
            StartPosition = FormStartPosition.CenterParent, KeyPreview = true };
        dialog.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Text = "Press a key or shortcut. Close this window to cancel." });
        string? captured = null;
        dialog.KeyDown += (_, e) => {
            e.Handled = e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            string key = e.KeyCode switch {
                >= Keys.D0 and <= Keys.D9 => ((int)e.KeyCode - (int)Keys.D0).ToString(),
                Keys.Return => "Enter", Keys.Prior => "PageUp", Keys.Next => "PageDown",
                _ => e.KeyCode.ToString()
            };
            var chord = (e.Control ? "Ctrl+" : "") + (e.Shift ? "Shift+" : "") +
                (e.Alt ? "Alt+" : "") + key;
            try { K8850Binding.Encode((int)_slot.Value, (int)_layer.Value, chord); }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message); return; }
            captured = chord;
            dialog.DialogResult = DialogResult.OK;
        };
        if (dialog.ShowDialog(this) == DialogResult.OK && captured != null)
        {
            _profile.Bindings[BindingKey] = captured;
            ShowBinding();
        }
    }

    private void CopyAssignment()
    {
        if (!_profile.Bindings.TryGetValue(BindingKey, out var binding)) return;
        _copied = (_profile, BindingKey, binding, _profile.Labels.GetValueOrDefault(BindingKey, ""));
        _status.Text = $"Copied {_profile.Name} / {BindingKey}: {binding}. Select a destination.";
        RefreshLabels();
    }

    private static void SetLocalAssignment(K8850Profile profile, string key, string? binding, string? label)
    {
        if (binding is null) profile.Bindings.Remove(key);
        else profile.Bindings[key] = binding;
        if (string.IsNullOrWhiteSpace(label)) profile.Labels.Remove(key);
        else profile.Labels[key] = label;
    }

    private void PasteAssignment(bool swap)
    {
        if (_copied is not { } copy) return;
        string destination = BindingKey;
        if (swap && (!ReferenceEquals(copy.Profile, _profile) || copy.Key == destination ||
            !_profile.Bindings.ContainsKey(copy.Key) || !_profile.Bindings.ContainsKey(destination))) return;
        try
        {
            var binding = swap ? _profile.Bindings[copy.Key] : copy.Binding;
            K8850Binding.Encode((int)_slot.Value, (int)_layer.Value, binding);
            string label = swap ? _profile.Labels.GetValueOrDefault(copy.Key, "") : copy.Label;
            if (swap)
            {
                var parts = copy.Key.Split(':');
                K8850Binding.Encode(int.Parse(parts[1]), int.Parse(parts[0]), _profile.Bindings[destination]);
            }
            if (_profile.Bindings.ContainsKey(destination) &&
                MessageBox.Show(this, swap ? "Swap the two local assignments and labels?" : "Replace the destination local assignment and label?",
                    "Confirm local edit", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            if (swap)
                SetLocalAssignment(_profile, copy.Key, _profile.Bindings[destination], _profile.Labels.GetValueOrDefault(destination));
            SetLocalAssignment(_profile, destination, binding, label);
            if (swap) _copied = null;
            ShowBinding();
            _status.Text = swap ? "Swapped local assignments. Review before uploading." : "Pasted local assignment. Review before uploading.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message); }
    }

    private void RefreshLabels()
    {
        foreach (var (slot, button) in _buttons)
        {
            var key = $"{_layer.Value}:{slot}";
            var label = slot <= 16 ? $"K{slot}" : $"R{(slot-17)/3+1} {new[] { "CCW", "Press", "CW" }[(slot-17)%3]}";
            var custom = _profile.Labels.GetValueOrDefault(key, "");
            string assignment = _profile.Bindings.TryGetValue(key, out var value)
                ? value + (Changed(key, value) ? " *" : " [sent]") : "Unknown";
            button.Text = label + "\n" + (string.IsNullOrWhiteSpace(custom) ? assignment : custom + "\n" + assignment);
            var matches = value != null ? _minecraftBindings?.Match(value) : null;
            string tooltip = button.Text;
            if (matches is { Count: > 0 })
                tooltip += "\n\nMinecraft saved bindings:\n" + string.Join("\n", matches);
            _tooltips.SetToolTip(button, tooltip);
        }
        _copy.Enabled = _profile.Bindings.ContainsKey(BindingKey);
        _paste.Enabled = _copied.HasValue;
        _swap.Enabled = _copied is { } copy && ReferenceEquals(copy.Profile, _profile) &&
            copy.Key != BindingKey && _profile.Bindings.ContainsKey(copy.Key) && _profile.Bindings.ContainsKey(BindingKey);
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

    private bool ReviewUpload(KeyValuePair<string, string>[] entries, string warning)
    {
        using var dialog = new Form { Text = "Review hardware upload", Size = new Size(850, 500),
            StartPosition = FormStartPosition.CenterParent };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false };
        foreach (var title in new[] { "Layer:slot", "Action label", "Last sent (not readback)", "New assignment" })
            grid.Columns.Add(title, title);
        foreach (var entry in entries.OrderBy(x => int.Parse(x.Key.Split(':')[0])).ThenBy(x => int.Parse(x.Key.Split(':')[1])))
            grid.Rows.Add(entry.Key, _profile.Labels.GetValueOrDefault(entry.Key, ""),
                _store.LastSent.GetValueOrDefault(SentKey(entry.Key), "Unknown"), entry.Value);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        var upload = new Button { Text = "Confirm hardware upload", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        footer.Controls.AddRange(new Control[] { upload, cancel });
        var info = new Label { Dock = DockStyle.Top, Height = 85,
            Text = $"Write {entries.Length} configured assignment(s). Unknown slots are not written. Last sent values are local history, not hardware readback." + warning };
        dialog.Controls.Add(grid);
        dialog.Controls.Add(footer);
        dialog.Controls.Add(info);
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK;
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
            if (reports.Any(r => r[3] > 3))
                throw new InvalidOperationException("Profile contains unsupported layers above 3. No bindings were written.");
            string layerWarning = reports.Any(r => r[3] != 1)
                ? "\r\n\r\nIncludes layers 2 or 3. Use the physical layer button to activate them." : "";
            if (!ReviewUpload(entries, layerWarning)) return;
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
