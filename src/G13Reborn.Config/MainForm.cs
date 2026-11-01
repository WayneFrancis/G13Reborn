using G13Reborn.Device;
using G13Reborn.Device.Hid;
using G13Reborn.Mapping;
using G13Reborn.VirtualController;

namespace G13Reborn.Config;

/// <summary>
/// G13 configuration and opt-in input forwarding. The diagnostics app remains
/// separate for protocol capture and troubleshooting.
/// </summary>
public sealed class MainForm : Form
{
    private readonly ComboBox _profiles = new() { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _addProfile = new() { Text = "New / Clone...", AutoSize = true };
    private readonly Button _deleteProfile = new() { Text = "Delete", AutoSize = true };
    private readonly Button _saveProfile = new() { Text = "Save profile", AutoSize = true };
    private readonly Button _editMacros = new() { Text = "Edit macros...", AutoSize = true };
    private readonly CheckBox _autoSwitch = new() { Text = "Auto-switch by foreground app", AutoSize = true };
    private readonly TextBox _associatedExecutables = new()
    {
        Width = 295, PlaceholderText = "notepad.exe, kenshi_x64.exe"
    };
    private readonly Label _activeProfileInfo = new()
    {
        AutoSize = true, Text = "Active: --", Padding = new Padding(3, 7, 0, 0)
    };
    private readonly ComboBox _devices = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _hardware = new()
    {
        Text = "LCD / backlight...", AutoSize = true
    };
    private readonly Button _start = new() { Text = "Start forwarding", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop / release", AutoSize = true };
    private readonly CheckBox _keyboardMouse = new()
    {
        Text = "Enable keyboard/mouse injection", AutoSize = true
    };
    private readonly CheckBox _vJoyEnabled = new()
    {
        Text = "Enable vJoy X/Y and buttons", AutoSize = true, Checked = true
    };
    private readonly NumericUpDown _deadzone = new()
    {
        Minimum = 0, Maximum = 30, Value = 10, Width = 48
    };
    private readonly CheckBox _invertY = new() { Text = "Invert Y", AutoSize = true };
    private readonly CheckBox _useRgb = new()
    {
        Text = "Use profile RGB", AutoSize = true
    };
    private readonly Button _pickRgb = new()
    {
        Text = "RGB colour...", AutoSize = true,
        BackColor = Color.FromArgb(0x5A, 0xFF, 0x6E)
    };
    private readonly CheckBox _lcdStatus = new()
    {
        Text = "Show LCD status", AutoSize = true
    };
    private readonly CheckBox _enableLayers = new()
    {
        Text = "Enable M1/M2/M3 layers", AutoSize = true
    };
    private readonly ComboBox _editLayer = new()
    {
        Width = 90, DropDownStyle = ComboBoxStyle.DropDownList
    };
    private readonly DataGridView _grid = new();
    private readonly Label _status = new() { AutoSize = true, Text = "Stopped." };
    private readonly Label _activity = new() { AutoSize = true, Text = "No input yet." };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly object _gate = new();

    private ProfilesDocument _document = new();
    private string _currentName = "Default";
    private bool _loading;
    private bool _dirty;
    // Editor changes are staged separately from the persisted profile.
    // Discarding a dirty profile must not silently save unsaved macros.
    private List<MacroDefinition>? _pendingMacros;
    private Dictionary<int, Dictionary<int, ButtonAction>> _pendingLayerMaps = new();
    private int _editingLayer = 1;
    private int _activeLayer = 1;
    private readonly G13LayerSelector _layerSelector = new();
    private bool _layerLedWarningShown;
    private bool _running;
    private bool _nativeOutputEnabled;
    private MappingProfile? _activeProfile;
    private ButtonTransitionEngine? _engine;
    private MacroTriggerDetector? _macroDetector;
    private readonly InputOutputRouter _output = new();
    private readonly DeferredMappedInput _deferredNormal = new();
    private CancellationTokenSource? _macroCancel;
    private Task? _macroTask;
    private string? _playingMacro;
    private int _playingMacroBit = -1;
    private readonly ButtonReleaseGate _releaseGate = new();
    private G13InputReport? _lastReport;
    private DateTime _nextForegroundPoll = DateTime.MinValue;
    private string? _foregroundName;
    private G13InputReader? _reader;
    private G13DeviceInfo? _rgbOutputDevice;
    private G13DeviceInfo? _lcdOutputDevice;
    private DateTime _nextLcdWrite = DateTime.MinValue;
    private bool _lcdBusy;
    private bool _lcdFailed;
    private VJoyDevice? _vJoy;
    private CancellationTokenSource? _cancellation;
    private Task? _readTask;
    private long _inputReports;
    private long _forwardedReports;
    private int _lastX;
    private int _lastY;
    private int _pressedCount;

    private sealed record DeviceChoice(G13DeviceInfo Info)
    {
        public override string ToString() =>
            $"G13 input {Info.InputReportLength} bytes — " +
            (Info.Path.Length > 34 ? Info.Path[^34..] : Info.Path);
    }

    public MainForm()
    {
        Text = "G13Reborn — Profiles & Input Mapping";
        Size = new Size(1050, 800);
        MinimumSize = new Size(880, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(root);

        var profileBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        profileBar.Controls.AddRange(new Control[]
        {
            TextLabel("Profile"), _profiles, _addProfile, _deleteProfile,
            _saveProfile, _editMacros
        });
        root.Controls.Add(profileBar, 0, 0);

        var associationBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false
        };
        associationBar.Controls.AddRange(new Control[]
        {
            _autoSwitch, TextLabel("Profile executables"), _associatedExecutables, _activeProfileInfo
        });
        root.Controls.Add(associationBar, 0, 1);

        var deviceBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        deviceBar.Controls.AddRange(new Control[]
        {
            TextLabel("G13 HID"), _devices, _refresh, _hardware, _start, _stop
        });
        root.Controls.Add(deviceBar, 0, 2);

        var optionsBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        optionsBar.Controls.AddRange(new Control[]
        {
            _vJoyEnabled, _keyboardMouse, TextLabel("Deadzone %"), _deadzone,
            _invertY, _useRgb, _pickRgb, _lcdStatus
        });
        root.Controls.Add(optionsBar, 0, 3);

        var layersBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false
        };
        _editLayer.Items.AddRange(new object[] { "M1", "M2", "M3" });
        _editLayer.SelectedIndex = 0;
        layersBar.Controls.AddRange(new Control[]
        {
            _enableLayers, TextLabel("Edit mapping layer"), _editLayer,
            TextLabel("M keys select a layer when forwarding; active G-keys are released on switch.")
        });
        root.Controls.Add(layersBar, 0, 4);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Physical", HeaderText = "Physical G13 control",
            ReadOnly = true, FillWeight = 45
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Bit", HeaderText = "HID bit", ReadOnly = true, FillWeight = 17
        });
        var actions = new DataGridViewComboBoxColumn
        {
            Name = "Kind", HeaderText = "Output action",
            FlatStyle = FlatStyle.Flat, FillWeight = 42
        };
        foreach (string kind in Enum.GetNames<MappingKind>())
            actions.Items.Add(kind);
        _grid.Columns.Add(actions);
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Parameter", HeaderText = "Key chord / vJoy # / Macro",
            FillWeight = 72
        });
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => MappingCellChanged(e);
        _grid.EditingControlShowing += (_, e) =>
        {
            if (_grid.CurrentCell?.OwningColumn?.Name == "Parameter" &&
                e.Control is DataGridViewComboBoxEditingControl macroEditor)
            {
                // A Macro parameter is a chosen profile-owned macro, not
                // arbitrary text. A missing macro must be added in Edit macros.
                macroEditor.DropDownStyle = ComboBoxStyle.DropDownList;
            }
        };
        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            _status.Text = "Invalid cell value: " + e.Exception?.Message;
        };
        root.Controls.Add(_grid, 0, 5);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _status.Dock = DockStyle.Fill;
        _activity.Dock = DockStyle.Fill;
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(_activity, 0, 1);
        root.Controls.Add(bottom, 0, 6);

        _profiles.SelectionChangeCommitted += (_, _) => ProfileSelected();
        _addProfile.Click += (_, _) => CloneProfile();
        _deleteProfile.Click += (_, _) => DeleteProfile();
        _saveProfile.Click += (_, _) => SaveCurrentProfile();
        _editMacros.Click += (_, _) => EditMacrosClicked();
        _refresh.Click += (_, _) => RefreshDevices();
        _hardware.Click += (_, _) =>
        {
            if (_running) return;
            using var dialog = new G13HardwareDialog();
            dialog.ShowDialog(this);
        };
        _start.Click += (_, _) => StartForwarding();
        _stop.Click += async (_, _) => await StopForwardingAsync();
        _deadzone.ValueChanged += (_, _) => MarkDirty();
        _invertY.CheckedChanged += (_, _) => MarkDirty();
        _autoSwitch.CheckedChanged += (_, _) => MarkDirty();
        _associatedExecutables.TextChanged += (_, _) => MarkDirty();
        _enableLayers.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            if (_editingLayer != 1 && !_enableLayers.Checked)
            {
                CaptureLayerGrid();
                _editingLayer = 1;
                _editLayer.SelectedIndex = 0;
                LoadLayerGrid();
            }
            UpdateControlState();
            RefreshSelectorRows();
            MarkDirty();
        };
        _editLayer.SelectionChangeCommitted += (_, _) =>
        {
            if (_running || _loading || !_enableLayers.Checked) return;
            CaptureLayerGrid();
            _editingLayer = _editLayer.SelectedIndex + 1;
            LoadLayerGrid();
        };
        _useRgb.CheckedChanged += (_, _) => MarkDirty();
        _lcdStatus.CheckedChanged += (_, _) => MarkDirty();
        _pickRgb.Click += (_, _) =>
        {
            if (_running) return;
            using var picker = new ColorDialog
            {
                Color = _pickRgb.BackColor,
                FullOpen = true
            };
            if (picker.ShowDialog(this) == DialogResult.OK)
            {
                _pickRgb.BackColor = picker.Color;
                MarkDirty();
            }
        };

        try
        {
            _document = ProfileStore.Load();
        }
        catch (Exception ex)
        {
            _document = new ProfilesDocument();
            MessageBox.Show(this,
                "Failed to load profiles. In-memory defaults will be used.\r\n" +
                "The existing profile file has not been overwritten.\r\n\r\n" + ex.Message,
                "G13Reborn profiles", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _currentName = _document.SelectedProfile;
        UpdateProfileList();
        LoadProfile(_currentName);
        RefreshDevices();
        UpdateControlState();
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
    }

    private static Label TextLabel(string text) => new()
    {
        Text = text, AutoSize = true, Padding = new Padding(4, 7, 4, 0)
    };

    private MappingProfile Current() =>
        _document.Profiles.Single(p => string.Equals(
            p.Name, _currentName, StringComparison.OrdinalIgnoreCase));

    private void UpdateProfileList()
    {
        _loading = true;
        try
        {
            _profiles.Items.Clear();
            foreach (var p in _document.Profiles.OrderBy(p => p.Name))
                _profiles.Items.Add(p.Name);
            _profiles.SelectedItem = _currentName;
        }
        finally { _loading = false; }
    }

    private void LoadProfile(string name)
    {
        _loading = true;
        try
        {
            _currentName = name;
            _pendingMacros = null;
            var profile = Current();
            _profiles.SelectedItem = profile.Name;
            _deadzone.Value = profile.DeadzonePercent;
            _invertY.Checked = profile.InvertY;
            _useRgb.Checked = profile.UseProfileRgb;
            _pickRgb.BackColor = Color.FromArgb(
                (int)((profile.ProfileRgb >> 16) & 0xFF),
                (int)((profile.ProfileRgb >> 8) & 0xFF),
                (int)(profile.ProfileRgb & 0xFF));
            _lcdStatus.Checked = _document.LcdStatusEnabled;
            _associatedExecutables.Text = string.Join(", ", profile.ExecutableNames);
            _autoSwitch.Checked = _document.AutoSwitchEnabled;
            _pendingLayerMaps = new()
            {
                [1] = MappingProfile.CloneButtons(profile.Buttons),
                [2] = MappingProfile.CloneButtons(
                    !profile.UseMappingLayers && profile.M2Buttons.Count == 0
                        ? profile.Buttons : profile.M2Buttons),
                [3] = MappingProfile.CloneButtons(
                    !profile.UseMappingLayers && profile.M3Buttons.Count == 0
                        ? profile.Buttons : profile.M3Buttons)
            };
            _editingLayer = 1;
            _enableLayers.Checked = profile.UseMappingLayers;
            _editLayer.SelectedIndex = 0;
            LoadLayerGrid();
            _dirty = false;
            _status.Text = $"Profile '{profile.Name}' loaded. Input output is stopped.";
            UpdateControlState();
        }
        finally { _loading = false; }
    }

    private Dictionary<int, ButtonAction> ReadGridBindings()
    {
        _grid.EndEdit();
        var result = new Dictionary<int, ButtonAction>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            int bit = (int)row.Tag!;
            result[bit] = new ButtonAction
            {
                Kind = Enum.Parse<MappingKind>(
                    row.Cells["Kind"].Value?.ToString() ?? "None"),
                Parameter = row.Cells["Parameter"].Value?.ToString()?.Trim() ?? ""
            };
        }
        return result;
    }

    private void CaptureLayerGrid()
    {
        if (_grid.Rows.Count > 0)
            _pendingLayerMaps[_editingLayer] = ReadGridBindings();
    }

    private void LoadLayerGrid()
    {
        bool previousLoading = _loading;
        _loading = true;
        try
        {
            _grid.Rows.Clear();
            var bindings = _pendingLayerMaps[_editingLayer];
            foreach (var button in G13ButtonMappings.PhysicalButtons)
            {
                ButtonAction mapping = bindings.GetValueOrDefault(button.Bit) ??
                    new ButtonAction { Kind = MappingKind.None };
                int index = _grid.Rows.Add(button.Name, button.Bit,
                    mapping.Kind.ToString(), mapping.Parameter);
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = button.Bit;
                row.Cells["Kind"].Tag = mapping.Kind;
                SetParameterCell(row, mapping.Kind, mapping.Parameter);
            }
            RefreshSelectorRows();
        }
        finally { _loading = previousLoading; }
    }

    private void RefreshSelectorRows()
    {
        bool reserved = _enableLayers.Checked;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            int bit = (int)row.Tag!;
            bool selector = reserved &&
                G13MappingLayers.SelectorBits.Contains(bit);
            row.Cells["Kind"].ReadOnly = selector;
            row.Cells["Parameter"].ReadOnly = selector;
            row.Cells["Physical"].Value =
                G13ButtonMappings.PhysicalButtons
                    .First(x => x.Bit == bit).Name +
                (selector ? " (layer selector)" : "");
            row.DefaultCellStyle.BackColor = selector
                ? SystemColors.Control : _grid.DefaultCellStyle.BackColor;
        }
    }

    /// <summary>
    /// Per-row parameter editor: macro actions use a dropdown of names in
    /// this profile; every other action retains its ordinary text entry.
    /// Rows must have their Kind and Tag initialized before calling this.
    /// </summary>
    private void SetParameterCell(
        DataGridViewRow row, MappingKind kind, string? parameter)
    {
        int index = _grid.Columns["Parameter"]?.Index ??
            throw new InvalidOperationException(
                "The mapping grid is missing its Parameter column.");
        if (kind == MappingKind.Macro)
        {
            string[] names = MappingParameterChoices.MacroNames(
                _pendingMacros ?? Current().Macros);
            var cell = new DataGridViewComboBoxCell
            {
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                DisplayStyleForCurrentCellOnly = false,
                ToolTipText = names.Length == 0
                    ? "Create a macro using Edit macros before assigning it."
                    : "Select a macro defined in this profile."
            };
            // Blank is deliberately allowed while configuring a mapping;
            // profile validation requires a real macro before saving.
            cell.Items.Add("");
            foreach (string name in names)
                cell.Items.Add(name);
            row.Cells[index] = cell;
            cell.Value = MappingParameterChoices.ExistingMacroChoice(
                parameter, names);
        }
        else
        {
            var cell = new DataGridViewTextBoxCell
            {
                Value = parameter ?? ""
            };
            row.Cells[index] = cell;
        }
    }

    private void MappingCellChanged(DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        if (e.ColumnIndex == (_grid.Columns["Kind"]?.Index ??
            throw new InvalidOperationException(
                "The mapping grid is missing its Kind column.")))
        {
            DataGridViewRow row = _grid.Rows[e.RowIndex];
            if (Enum.TryParse<MappingKind>(
                row.Cells["Kind"].Value?.ToString(), out var newKind))
            {
                MappingKind oldKind = row.Cells["Kind"].Tag is MappingKind old
                    ? old : MappingKind.None;
                string existing = row.Cells["Parameter"].Value?.ToString() ?? "";
                string next = MappingParameterChoices.ParameterAfterKindChange(
                    oldKind, newKind, existing);
                _loading = true;
                try
                {
                    SetParameterCell(row, newKind, next);
                    row.Cells["Kind"].Tag = newKind;
                }
                finally { _loading = false; }
                if (newKind == MappingKind.Macro &&
                    MappingParameterChoices.MacroNames(
                        _pendingMacros ?? Current().Macros).Length == 0)
                    _status.Text = "No macros defined. Use Edit macros to create one.";
            }
        }
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (_loading || _running) return;
        _dirty = true;
        _status.Text = $"Profile '{_currentName}' has unsaved changes.";
    }

    private void EditMacrosClicked()
    {
        if (_running) return;
        var previousMacros = _pendingMacros ?? Current().Macros;
        using var editor = new MacroEditorDialog(previousMacros);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is null)
            return;

        CaptureLayerGrid();
        var updatedMacros = editor.Result.Select(m => m.Clone()).ToList();
        var renames = editor.Renames ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var updatedLayers = new Dictionary<int, Dictionary<int, ButtonAction>>();
        int renamed = 0;
        int cleared = 0;
        foreach (int layer in new[] { 1, 2, 3 })
        {
            BindingReconciliation mapped = MacroCatalogEdits.ReconcileBindings(
                _pendingLayerMaps[layer],
                previousMacros.Select(m => m.Name).ToArray(),
                updatedMacros, renames);
            updatedLayers[layer] = mapped.Buttons;
            renamed += mapped.RenamedBits.Count;
            cleared += mapped.ClearedBits.Count;
        }

        if (cleared > 0 &&
            MessageBox.Show(this,
                $"Deleting macros will remove {cleared} G-key layer " +
                "assignment(s). Continue?",
                "Remove assigned macros", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _pendingMacros = updatedMacros;
        _pendingLayerMaps = updatedLayers;
        LoadLayerGrid();
        MarkDirty();
        _status.Text = "Macro changes staged. Save profile to persist them. " +
            $"Updated {renamed} layer assignment(s); " +
            $"cleared {cleared} deleted-macro layer assignment(s).";
    }

    private bool SaveCurrentProfile()
    {
        if (_running) return false;
        try
        {
            CaptureLayerGrid();
            var replacement = Current().Clone(_currentName);
            replacement.UseMappingLayers = _enableLayers.Checked;
            replacement.Buttons = MappingProfile.CloneButtons(_pendingLayerMaps[1]);
            replacement.M2Buttons = MappingProfile.CloneButtons(_pendingLayerMaps[2]);
            replacement.M3Buttons = MappingProfile.CloneButtons(_pendingLayerMaps[3]);
            if (_pendingMacros is not null)
                replacement.Macros = _pendingMacros.Select(m => m.Clone()).ToList();
            replacement.DeadzonePercent = (int)_deadzone.Value;
            replacement.InvertY = _invertY.Checked;
            replacement.UseProfileRgb = _useRgb.Checked;
            Color chosen = _pickRgb.BackColor;
            replacement.ProfileRgb =
                ((uint)chosen.R << 16) | ((uint)chosen.G << 8) | chosen.B;
            replacement.ExecutableNames = _associatedExecutables.Text
                .Split(new[] { ',', ';', '\r', '\n' },
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(ExecutableProfileMatcher.NormalizeExecutableName)
                .ToList();
            replacement.Validate();

            int index = _document.Profiles.FindIndex(p => string.Equals(
                p.Name, _currentName, StringComparison.OrdinalIgnoreCase));
            var previous = _document.Profiles[index];
            _document.Profiles[index] = replacement;
            _document.SelectedProfile = _currentName;
            bool previousAutoSwitch = _document.AutoSwitchEnabled;
            bool previousLcdStatus = _document.LcdStatusEnabled;
            _document.AutoSwitchEnabled = _autoSwitch.Checked;
            _document.LcdStatusEnabled = _lcdStatus.Checked;
            try { ProfileStore.Save(_document); }
            catch
            {
                _document.Profiles[index] = previous;
                _document.AutoSwitchEnabled = previousAutoSwitch;
                _document.LcdStatusEnabled = previousLcdStatus;
                throw;
            }
            _pendingMacros = null;
            _dirty = false;
            _status.Text = $"Saved '{_currentName}' to {ProfileStore.FilePath}";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not save profile",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private bool ResolveUnsavedChanges()
    {
        if (!_dirty) return true;
        var response = MessageBox.Show(this,
            $"Save changes to '{_currentName}'?", "Unsaved profile",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (response == DialogResult.Cancel) return false;
        if (response == DialogResult.Yes) return SaveCurrentProfile();
        return true;
    }

    private void ProfileSelected()
    {
        if (_loading) return;
        string next = _profiles.SelectedItem?.ToString() ?? _currentName;
        if (_running || !ResolveUnsavedChanges())
        {
            _profiles.SelectedItem = _currentName;
            return;
        }
        if (next == _currentName) return;
        _document.SelectedProfile = next;
        try
        {
            ProfileStore.Save(_document);
            LoadProfile(next);
        }
        catch (Exception ex)
        {
            _document.SelectedProfile = _currentName;
            _profiles.SelectedItem = _currentName;
            MessageBox.Show(this, ex.Message, "Could not select profile");
        }
    }

    private void CloneProfile()
    {
        if (_running || !ResolveUnsavedChanges()) return;
        string? name = AskForName();
        if (name is null) return;
        if (string.IsNullOrWhiteSpace(name) ||
            _document.Profiles.Any(p => string.Equals(p.Name, name,
                StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Enter a unique profile name.", "Invalid name");
            return;
        }

        var clone = Current().Clone(name.Trim());
        // Profiles may not compete for the same foreground executable.
        clone.ExecutableNames.Clear();
        _document.Profiles.Add(clone);
        _document.SelectedProfile = clone.Name;
        try
        {
            ProfileStore.Save(_document);
            _currentName = clone.Name;
            UpdateProfileList();
            LoadProfile(_currentName);
        }
        catch (Exception ex)
        {
            _document.Profiles.Remove(clone);
            _document.SelectedProfile = _currentName;
            MessageBox.Show(this, ex.Message, "Could not create profile");
        }
    }

    private void DeleteProfile()
    {
        if (_running || _document.Profiles.Count <= 1) return;
        if (MessageBox.Show(this, $"Delete profile '{_currentName}'?",
            "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) !=
            DialogResult.Yes) return;
        var old = Current();
        _document.Profiles.Remove(old);
        _document.SelectedProfile = _document.Profiles[0].Name;
        try
        {
            ProfileStore.Save(_document);
            _currentName = _document.SelectedProfile;
            UpdateProfileList();
            LoadProfile(_currentName);
        }
        catch (Exception ex)
        {
            _document.Profiles.Add(old);
            _document.SelectedProfile = old.Name;
            MessageBox.Show(this, ex.Message, "Could not delete profile");
        }
    }

    private string? AskForName()
    {
        using var dialog = new Form
        {
            Text = "New profile", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(400, 116), MaximizeBox = false, MinimizeBox = false
        };
        var name = new TextBox { Left = 14, Top = 15, Width = 369, Text = _currentName + " copy" };
        var ok = new Button
        {
            Text = "Create", Left = 205, Top = 61, Width = 83,
            DialogResult = DialogResult.OK
        };
        var cancel = new Button
        {
            Text = "Cancel", Left = 296, Top = 61, Width = 83,
            DialogResult = DialogResult.Cancel
        };
        dialog.Controls.AddRange(new Control[] { name, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? name.Text : null;
    }

    private void RefreshDevices()
    {
        if (_running) return;
        try
        {
            _devices.Items.Clear();
            foreach (var device in G13DeviceDiscovery.Enumerate()
                .Where(i => i.InputReportLength > 0))
                _devices.Items.Add(new DeviceChoice(device));
            if (_devices.Items.Count > 0) _devices.SelectedIndex = 0;
            _status.Text = _devices.Items.Count == 0
                ? "No readable G13 HID interface detected."
                : "G13 interface detected. Select outputs and click Start forwarding.";
        }
        catch (Exception ex)
        {
            _status.Text = "G13 enumeration failed: " + ex.Message;
        }
        UpdateControlState();
    }

    private void StartForwarding()
    {
        if (_running) return;
        if (_devices.SelectedItem is not DeviceChoice device)
        {
            MessageBox.Show(this, "Select a G13 HID interface.");
            return;
        }
        if (!_vJoyEnabled.Checked && !_keyboardMouse.Checked)
        {
            MessageBox.Show(this, "Enable at least one output type.");
            return;
        }
        if (!SaveCurrentProfile()) return;

        try
        {
            var profile = Current().Clone(Current().Name);
            var engine = new ButtonTransitionEngine(profile);
            // Enumerate output-capable HID collections only if hardware
            // status/colour output was explicitly enabled by the user.
            var outputs = profile.UseProfileRgb || _document.LcdStatusEnabled
                ? G13DeviceDiscovery.Enumerate()
                : Array.Empty<G13DeviceInfo>();
            G13DeviceInfo? rgb = outputs.FirstOrDefault(x =>
                x.FeatureReportLength is >= G13HardwareReports.FeatureLength and <= 4096);
            G13DeviceInfo? lcd = outputs.FirstOrDefault(x =>
                x.OutputReportLength == G13HardwareReports.LcdReportLength);
            // Open vJoy first, so a driver failure never starts keyboard injection.
            VJoyDevice? vjoy = _vJoyEnabled.Checked ? VJoyDevice.Open(1) : null;
            G13InputReader reader;
            try { reader = G13InputReader.Open(device.Info); }
            catch { vjoy?.Dispose(); throw; }

            var cancellation = new CancellationTokenSource();
            lock (_gate)
            {
                _activeProfile = profile;
                _engine = engine;
                _macroDetector = new MacroTriggerDetector(profile);
                _lastReport = null;
                _releaseGate.Clear();
                _vJoy = vjoy;
                _reader = reader;
                _rgbOutputDevice = rgb;
                _lcdOutputDevice = lcd;
                _lcdFailed = false;
                _nextLcdWrite = DateTime.MinValue;
                _nativeOutputEnabled = _keyboardMouse.Checked;
                _cancellation = cancellation;
            }
            _running = true;
            _nextForegroundPoll = DateTime.MinValue;
            _readTask = Task.Run(() => RunReaderAsync(reader, cancellation.Token));
            _status.Text = "Running: " + profile.Name +
                " — press Stop / release before modifying profiles.";
            ApplyProfileRgb(profile);
            UpdateControlState();
        }
        catch (Exception ex)
        {
            _status.Text = "Could not start: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Could not start forwarding",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunReaderAsync(G13InputReader reader, CancellationToken token)
    {
        try
        {
            await reader.ReadAsync(ProcessReport, token);
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)(async () =>
                {
                    if (IsDisposed) return;
                    await StopForwardingAsync();
                    _status.Text = "Forwarding stopped: " + ex.Message;
                }));
            }
            catch (InvalidOperationException) { }
        }
    }

    private void ProcessReport(byte[] bytes)
    {
        if (!G13InputReport.TryParse(bytes, out var report) || report is null)
            return;

        Interlocked.Increment(ref _inputReports);
        Volatile.Write(ref _lastX, report.X);
        Volatile.Write(ref _lastY, report.Y);
        int count = report.PressedGKeys.Count() + report.PressedAuxiliaryKeys.Count();
        Volatile.Write(ref _pressedCount, count);

        lock (_gate)
        {
            // Stop / release must prevent new input even if one last USB
            // report arrives before cancellation closes the HID stream.
            if (!Volatile.Read(ref _running))
                return;
            _lastReport = report;
            if (_activeProfile is null || _engine is null)
                return;
            // Held keys at a profile change cannot trigger newly assigned
            // actions until physically released and pressed again.
            G13InputReport safeReport = _releaseGate.Filter(report);
            if (_vJoy is not null)
            {
                _vJoy.Update(
                    report.X, report.Y, _activeProfile.DeadzonePercent / 100.0,
                    _activeProfile.InvertY);
                _vJoy.UpdateButtons(_activeProfile.GetVirtualButtons(
                    safeReport, _vJoy.ButtonCount));
            }
            if (_nativeOutputEnabled)
            {
                foreach (var action in _engine.Apply(safeReport))
                    foreach (var allowed in _deferredNormal.Receive(action))
                        _output.ApplyNormal(allowed);

                // Process BOTH press and release edges. A while-held
                // macro stops immediately on release; toggle stops on its
                // next fresh press. Repeated HID reports do not retrigger.
                // Only one macro can own the output at a time.
                if (_macroDetector is not null)
                    foreach (MacroButtonTransition transition in
                             _macroDetector.DetectTransitions(safeReport))
                    {
                        MacroDefinition? macro = _activeProfile.Macros.FirstOrDefault(m =>
                            string.Equals(m.Name, transition.MacroName,
                                StringComparison.OrdinalIgnoreCase));
                        if (macro is null) continue;
                        bool isActive = _macroCancel is not null &&
                            _playingMacroBit == transition.Bit;
                        MacroRepeatAction action = MacroRepeatPolicy.OnTransition(
                            macro.RepeatMode, transition.IsPressed, isActive);
                        if (action == MacroRepeatAction.Stop)
                            CancelMacroLocked();
                        else if (action == MacroRepeatAction.Start)
                            TryStartMacroLocked(macro.Clone(), transition.Bit);
                    }
            }
            Interlocked.Increment(ref _forwardedReports);
        }
    }

    private void TryStartMacroLocked(MacroDefinition macro, int physicalBit)
    {
        // Called under _gate; do not queue overlapping macros. A retrigger
        // requires a physical release and a fresh press.
        if (_macroCancel is not null || !_nativeOutputEnabled)
            return;

        var cancellation = new CancellationTokenSource();
        _macroCancel = cancellation;
        _playingMacro = macro.Name;
        _playingMacroBit = physicalBit;
        if (macro.Mode == MacroMode.Text)
            _deferredNormal.Begin();
        bool finishedSuccessfully = false;
        _macroTask = Task.Run(async () =>
        {
            try
            {
                await MacroRepeatRunner.ExecuteAsync(macro,
                    action =>
                    {
                        lock (_gate)
                            if (ReferenceEquals(_macroCancel, cancellation) &&
                                !cancellation.IsCancellationRequested)
                                _output.ApplyMacro(action);
                    },
                    text =>
                    {
                        // One lock + one SendInput array per text chunk.
                        // G1 keyboard events cannot interleave between
                        // letters of the same macro text chunk.
                        lock (_gate)
                            if (ReferenceEquals(_macroCancel, cancellation) &&
                                !cancellation.IsCancellationRequested)
                                Win32InputSender.SendUnicodeText(text);
                    },
                    () =>
                    {
                        // A pass can intentionally finish with held
                        // keys/mouse buttons. Release before repeating.
                        lock (_gate)
                            if (ReferenceEquals(_macroCancel, cancellation) &&
                                !cancellation.IsCancellationRequested)
                                _output.ReleaseMacro();
                    },
                    cancellation.Token);
                finishedSuccessfully = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((MethodInvoker)(() =>
                        {
                            if (!IsDisposed)
                                _status.Text = $"Macro '{macro.Name}' failed: {ex.Message}";
                        }));
                }
                catch (InvalidOperationException) { }
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_macroCancel, cancellation))
                    {
                        // A sequence may leave a KeyDown/MouseDown deliberately
                        // held. Release it even after successful completion.
                        try { _output.ReleaseMacro(); }
                        catch { /* best effort if Windows rejects the release */ }

                        // The normal G-key events pressed during text typing
                        // are replayed only after successful completion.
                        // On cancel or error, discard them; never replay into
                        // another foreground application.
                        IReadOnlyList<InputEvent> deferred =
                            _deferredNormal.End(finishedSuccessfully &&
                                !cancellation.IsCancellationRequested);
                        foreach (InputEvent action in deferred)
                        {
                            try { _output.ApplyNormal(action); }
                            catch { /* macro failure is already handled */ }
                        }

                        _macroCancel = null;
                        _macroTask = null;
                        _playingMacro = null;
                        _playingMacroBit = -1;
                    }
                }
                cancellation.Dispose();
            }
        });
    }

    /// <summary>Must be called under _gate.</summary>
    private void CancelMacroLocked()
    {
        CancellationTokenSource? cancellation = _macroCancel;
        _macroCancel = null;
        _macroTask = null;
        _playingMacro = null;
        _playingMacroBit = -1;
        cancellation?.Cancel();
        _deferredNormal.End(replay: false);
        try { _output.ReleaseMacro(); }
        catch { /* best effort: still reset the rest of the output */ }
    }

    private async Task StopForwardingAsync()
    {
        if (!_running) return;
        Volatile.Write(ref _running, false);
        _start.Enabled = false;
        _stop.Enabled = false;
        var cancellation = _cancellation;
        var reader = _reader;
        var readTask = _readTask;
        lock (_gate) CancelMacroLocked();
        cancellation?.Cancel();
        reader?.Dispose();
        try
        {
            if (readTask is not null)
                await readTask;
        }
        catch (Exception) { /* stopping / input errors are handled by the reader */ }
        cancellation?.Dispose();
        lock (_gate)
        {
            if (_engine is not null && _nativeOutputEnabled)
            {
                foreach (var command in _engine.ReleaseAll())
                {
                    try { _output.ApplyNormal(command); }
                    catch { /* best effort on shutdown */ }
                }
                try { _output.ReleaseAll(); }
                catch { /* best effort */ }
            }
            _vJoy?.Dispose();
            _vJoy = null;
            _engine = null;
            _macroDetector = null;
            _activeProfile = null;
            _reader = null;
            _rgbOutputDevice = null;
            _lcdOutputDevice = null;
            _lcdFailed = false;
            _nextLcdWrite = DateTime.MinValue;
            _readTask = null;
            _cancellation = null;
            _nativeOutputEnabled = false;
            _lastReport = null;
            _releaseGate.Clear();
        }
        _status.Text = "Stopped. All injected keys/buttons released.";
        UpdateControlState();
    }

    private void RefreshStatus()
    {
        if (_running && DateTime.UtcNow >= _nextForegroundPoll)
        {
            _nextForegroundPoll = DateTime.UtcNow.AddMilliseconds(300);
            _foregroundName = ForegroundExecutable.TryGetName();
            MappingProfile selected = ExecutableProfileMatcher.Resolve(
                _document, _currentName, _foregroundName, _autoSwitch.Checked);
            try
            {
                SwitchActiveProfile(selected);
            }
            catch (Exception ex)
            {
                // Native output has failed. Stop feeding inputs and report why.
                _ = StopAfterProfileErrorAsync(ex);
            }
        }
        string activeName;
        string? playingMacro;
        lock (_gate)
        {
            activeName = _activeProfile?.Name ?? "(stopped)";
            playingMacro = _playingMacro;
        }
        _activeProfileInfo.Text = "Active: " + activeName;
        _activity.Text =
            $"HID input reports: {Interlocked.Read(ref _inputReports):N0}  |  " +
            $"Processed reports: {Interlocked.Read(ref _forwardedReports):N0}  |  " +
            $"Stick X/Y: {Volatile.Read(ref _lastX)}/{Volatile.Read(ref _lastY)}  |  " +
            $"Pressed controls: {Volatile.Read(ref _pressedCount)}" +
            (playingMacro is null ? "" : $"  |  Macro: {playingMacro}");

        if (_running && _document.LcdStatusEnabled &&
            !_lcdFailed && !_lcdBusy && _lcdOutputDevice is not null &&
            DateTime.UtcNow >= _nextLcdWrite)
        {
            _nextLcdWrite = DateTime.UtcNow.AddSeconds(1);
            _ = UpdateLcdStatusAsync(activeName, playingMacro);
        }
    }

    private async Task UpdateLcdStatusAsync(string profile, string? macro)
    {
        if (_lcdOutputDevice is not G13DeviceInfo device || _lcdBusy || !_running)
            return;
        CancellationToken token = _cancellation?.Token ?? CancellationToken.None;
        _lcdBusy = true;
        try
        {
            byte[] frame = G13LcdStatusRenderer.Render(
                profile, macro, DateTime.Now);
            token.ThrowIfCancellationRequested();
            await G13OutputDevice.SendLcdFrameAsync(device, frame, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stop / release cancelled an in-flight write.
        }
        catch (Exception ex)
        {
            // LCD is optional; keep keyboard and mouse forwarding alive.
            _lcdFailed = true;
            if (!IsDisposed && _running)
                _status.Text = "LCD status disabled until next Start: " + ex.Message;
        }
        finally { _lcdBusy = false; }
    }

    private void ApplyProfileRgb(MappingProfile profile)
    {
        if (!profile.UseProfileRgb) return;
        if (_rgbOutputDevice is not G13DeviceInfo device)
        {
            _status.Text = "No G13 RGB feature interface found; forwarding remains active.";
            return;
        }
        try
        {
            G13OutputDevice.SetRgb(device,
                (byte)(profile.ProfileRgb >> 16),
                (byte)(profile.ProfileRgb >> 8),
                (byte)profile.ProfileRgb);
        }
        catch (Exception ex)
        {
            // Colour is cosmetic; do not stop input output or loop retries.
            _status.Text = "Profile RGB not applied: " + ex.Message;
        }
    }

    private void SwitchActiveProfile(MappingProfile selected)
    {
        lock (_gate)
        {
            if (_activeProfile is null ||
                string.Equals(_activeProfile.Name, selected.Name,
                    StringComparison.OrdinalIgnoreCase))
                return;

            // Cancel an in-flight macro before assigning another profile.
            // The worker checks cancellation under the same _gate, so it
            // cannot inject another event after this cancellation.
            CancelMacroLocked();
            // Release old output before accepting any input in the new profile.
            if (_engine is not null && _nativeOutputEnabled)
                foreach (InputEvent action in _engine.ReleaseAll())
                    _output.ApplyNormal(action);
            _vJoy?.UpdateButtons(0);

            _releaseGate.BlockCurrentlyHeld(_lastReport);
            _activeProfile = selected.Clone(selected.Name);
            _engine = new ButtonTransitionEngine(_activeProfile);
            _macroDetector = new MacroTriggerDetector(_activeProfile);
        }

        _status.Text = $"Switched to '{selected.Name}' for " +
            (_foregroundName ?? "an unknown application") + ".";
        _nextLcdWrite = DateTime.MinValue;
        ApplyProfileRgb(selected);
    }

    private async Task StopAfterProfileErrorAsync(Exception ex)
    {
        if (!_running) return;
        await StopForwardingAsync();
        if (!IsDisposed)
            _status.Text = "Automatic profile switch failed: " + ex.Message;
    }

    private void UpdateControlState()
    {
        _profiles.Enabled = !_running;
        _addProfile.Enabled = !_running;
        _deleteProfile.Enabled = !_running && _document.Profiles.Count > 1;
        _saveProfile.Enabled = !_running;
        _editMacros.Enabled = !_running;
        _hardware.Enabled = !_running;
        _autoSwitch.Enabled = !_running;
        _associatedExecutables.Enabled = !_running;
        _devices.Enabled = !_running;
        _refresh.Enabled = !_running;
        _keyboardMouse.Enabled = !_running;
        _vJoyEnabled.Enabled = !_running;
        _deadzone.Enabled = !_running;
        _invertY.Enabled = !_running;
        _useRgb.Enabled = !_running;
        _pickRgb.Enabled = !_running;
        _lcdStatus.Enabled = !_running;
        _editLayer.Enabled = !_running && _enableLayers.Checked;
        _enableLayers.Enabled = !_running;
        _grid.ReadOnly = _running;
        _start.Enabled = !_running && _devices.SelectedItem is not null;
        _stop.Enabled = _running;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Don't leave the feeder holding key/mouse inputs after a window close.
        // Stop synchronously on shutdown; cancellation/dispose breaks ReadAsync.
        _timer.Stop();
        if (_running)
        {
            Volatile.Write(ref _running, false);
            lock (_gate) CancelMacroLocked();
            _cancellation?.Cancel();
            _reader?.Dispose();
            try { _readTask?.GetAwaiter().GetResult(); }
            catch { }
            lock (_gate)
            {
                CancelMacroLocked();
                if (_engine is not null && _nativeOutputEnabled)
                {
                    foreach (var action in _engine.ReleaseAll())
                        try { _output.ApplyNormal(action); } catch { }
                    try { _output.ReleaseAll(); } catch { }
                }
                _vJoy?.Dispose();
                _vJoy = null;
                _lastReport = null;
                _releaseGate.Clear();
            }
            _cancellation?.Dispose();
            _running = false;
        }
        if (!_running && _dirty && !ResolveUnsavedChanges())
        {
            e.Cancel = true;
            _timer.Start();
        }
        base.OnFormClosing(e);
    }
}
