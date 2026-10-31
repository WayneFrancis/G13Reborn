using G13Reborn.Mapping;

namespace G13Reborn.Config;

/// <summary>
/// Edits profile-owned text and step-sequence macros. No live input is emitted.
/// A successful Save replaces the in-memory macros; Cancel leaves them intact.
/// </summary>
internal sealed class MacroEditorDialog : Form
{
    private readonly List<MacroDefinition> _macros;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _mode = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 135
    };
    private readonly NumericUpDown _textDelay = new()
    {
        Minimum = 0, Maximum = 100, Width = 55, Value = 0
    };
    private readonly TextBox _text = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
        AcceptsReturn = true, AcceptsTab = true
    };
    private readonly DataGridView _steps = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly Button _addStep = new() { Text = "Add step", AutoSize = true };
    private readonly Button _removeStep = new() { Text = "Remove step", AutoSize = true };
    private bool _loading;
    private int _loadedIndex = -1;

    public IReadOnlyList<MacroDefinition>? Result { get; private set; }

    public MacroEditorDialog(IEnumerable<MacroDefinition> existing)
    {
        _macros = existing.Select(m => m.Clone()).ToList();
        Text = "G13Reborn — Macro Editor";
        Size = new Size(940, 720);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(10),
            ColumnCount = 2, RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        left.Controls.Add(new Label { Text = "Profile macros", Dock = DockStyle.Fill }, 0, 0);
        left.Controls.Add(_list, 0, 1);
        var listButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var create = new Button { Text = "New", AutoSize = true };
        var delete = new Button { Text = "Delete", AutoSize = true };
        listButtons.Controls.AddRange(new Control[] { create, delete });
        left.Controls.Add(listButtons, 0, 2);
        root.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0),
            RowCount = 7, ColumnCount = 1
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        var nameBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        nameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        nameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nameBar.Controls.Add(new Label
        {
            Text = "Macro name:", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        nameBar.Controls.Add(_name, 1, 0);
        right.Controls.Add(nameBar, 0, 0);

        var modeBar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        modeBar.Controls.Add(new Label
        {
            Text = "Macro type:", AutoSize = true,
            Padding = new Padding(0, 7, 4, 0)
        });
        _mode.Items.AddRange(Enum.GetNames<MacroMode>());
        modeBar.Controls.Add(_mode);
        modeBar.Controls.Add(new Label
        {
            AutoSize = true, Text = "Text delay (ms/char):",
            Padding = new Padding(10, 7, 4, 0)
        });
        modeBar.Controls.Add(_textDelay);
        right.Controls.Add(modeBar, 0, 1);
        right.Controls.Add(new Label
        {
            Text = "Text to type (used only for Text macros):", Dock = DockStyle.Fill
        }, 0, 2);
        right.Controls.Add(_text, 0, 3);

        var kind = new DataGridViewComboBoxColumn
        {
            HeaderText = "Action", FillWeight = 48,
            FlatStyle = FlatStyle.Flat
        };
        kind.Items.AddRange(Enum.GetNames<MacroStepKind>());
        _steps.Columns.Add(kind);
        _steps.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Key / mouse button", FillWeight = 50
        });
        _steps.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Delay after (ms)", FillWeight = 38
        });
        _steps.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_steps.IsCurrentCellDirty)
                _steps.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _steps.DataError += (_, e) => { e.ThrowException = false; };
        right.Controls.Add(_steps, 0, 4);

        var stepButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        stepButtons.Controls.AddRange(new Control[] { _addStep, _removeStep });
        right.Controls.Add(stepButtons, 0, 5);
        right.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Step keys: A, F1, Ctrl, Shift, Enter...   Mouse: Left, Right, Middle. " +
                "Use KeyDown/KeyUp for held shortcuts. Delay is 0–60000 ms."
        }, 0, 6);
        root.Controls.Add(right, 1, 0);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft
        };
        var save = new Button { Text = "Save macros", Width = 120, Height = 32 };
        var cancel = new Button
        {
            Text = "Cancel", Width = 90, Height = 32,
            DialogResult = DialogResult.Cancel
        };
        bottom.Controls.AddRange(new Control[] { save, cancel });
        root.Controls.Add(bottom, 0, 1);
        root.SetColumnSpan(bottom, 2);
        CancelButton = cancel;

        _list.SelectedIndexChanged += (_, _) => SelectionChanged();
        _mode.SelectedIndexChanged += (_, _) => UpdateMode();
        create.Click += (_, _) => AddMacro();
        delete.Click += (_, _) => DeleteMacro();
        _addStep.Click += (_, _) => _steps.Rows.Add("KeyPress", "A", "0");
        _removeStep.Click += (_, _) =>
        {
            if (_steps.CurrentRow is not null)
                _steps.Rows.Remove(_steps.CurrentRow);
        };
        save.Click += (_, _) => SaveClicked();

        RefreshList(_macros.Count > 0 ? 0 : -1);
    }

    private void RefreshList(int index)
    {
        _loading = true;
        try
        {
            _list.Items.Clear();
            foreach (MacroDefinition macro in _macros)
                _list.Items.Add(macro.Name);
            _list.SelectedIndex = index;
        }
        finally { _loading = false; }
        LoadMacro(index);
    }

    private void LoadMacro(int index)
    {
        _loading = true;
        try
        {
            _loadedIndex = index;
            _name.Text = index >= 0 ? _macros[index].Name : "";
            _mode.SelectedItem = index >= 0
                ? _macros[index].Mode.ToString() : MacroMode.Text.ToString();
            _text.Text = index >= 0 ? _macros[index].Text : "";
            _textDelay.Value = index >= 0 ? Math.Clamp(
                _macros[index].TextDelayMs, 0, 100) : 0;
            _steps.Rows.Clear();
            if (index >= 0)
                foreach (MacroStep step in _macros[index].Steps)
                    _steps.Rows.Add(step.Kind.ToString(), step.Parameter,
                        step.DelayAfterMs.ToString());
            UpdateMode();
            _name.Enabled = _mode.Enabled = index >= 0;
        }
        finally { _loading = false; }
    }

    private void UpdateMode()
    {
        bool active = _loadedIndex >= 0;
        bool isText = _mode.SelectedItem?.ToString() == nameof(MacroMode.Text);
        _text.Enabled = active && isText;
        _textDelay.Enabled = active && isText;
        _steps.Enabled = active && !isText;
        _addStep.Enabled = _removeStep.Enabled = active && !isText;
    }

    private bool CommitCurrent()
    {
        if (_loadedIndex < 0) return true;
        try
        {
            _steps.EndEdit();
            var macro = _macros[_loadedIndex].Clone();
            macro.Name = _name.Text.Trim();
            macro.Mode = Enum.Parse<MacroMode>(_mode.SelectedItem?.ToString() ?? "Text");
            macro.Text = _text.Text;
            macro.TextDelayMs = (int)_textDelay.Value;
            macro.Steps.Clear();

            foreach (DataGridViewRow row in _steps.Rows)
            {
                var step = new MacroStep
                {
                    Kind = Enum.Parse<MacroStepKind>(row.Cells[0].Value?.ToString() ?? "Delay"),
                    Parameter = row.Cells[1].Value?.ToString()?.Trim() ?? "",
                    DelayAfterMs = int.Parse(row.Cells[2].Value?.ToString() ?? "0")
                };
                macro.Steps.Add(step);
            }
            macro.Validate();
            if (_macros.Where((_, index) => index != _loadedIndex).Any(m =>
                string.Equals(m.Name, macro.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Macro '{macro.Name}' already exists.");
            _macros[_loadedIndex] = macro;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid macro",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private void SelectionChanged()
    {
        if (_loading) return;
        int next = _list.SelectedIndex;
        if (next == _loadedIndex) return;
        if (!CommitCurrent())
        {
            _loading = true;
            _list.SelectedIndex = _loadedIndex;
            _loading = false;
            return;
        }
        RefreshList(next);
    }

    private void AddMacro()
    {
        if (!CommitCurrent()) return;
        int i = 1;
        while (_macros.Any(m => string.Equals(
            m.Name, $"Macro {i}", StringComparison.OrdinalIgnoreCase))) i++;

        _macros.Add(new MacroDefinition { Name = $"Macro {i}", Text = "Hello" });
        RefreshList(_macros.Count - 1);
    }

    private void DeleteMacro()
    {
        if (_loadedIndex < 0) return;
        int next = Math.Max(0, _loadedIndex - 1);
        _macros.RemoveAt(_loadedIndex);
        RefreshList(_macros.Count == 0 ? -1 : next);
    }

    private void SaveClicked()
    {
        if (!CommitCurrent()) return;
        try
        {
            foreach (MacroDefinition macro in _macros) macro.Validate();
            Result = _macros.Select(m => m.Clone()).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid macros",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
