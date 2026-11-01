using G13Reborn.Mapping;

namespace G13Reborn.Config;

/// <summary>
/// Edits profile-owned text and step-sequence macros. Live output is
/// emitted only through the explicitly invoked, cancellable test dialog.
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
        Minimum = 5, Maximum = 100, Width = 55, Value = 25
    };
    private readonly TextBox _text = new()
    {
        Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
        AcceptsReturn = true, AcceptsTab = true
    };
    private readonly DataGridView _steps = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = true,
        RowHeadersWidth = 48,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly Button _addStep = new() { Text = "Add step (after)", AutoSize = true };
    private readonly Button _insertBefore = new()
    {
        Text = "Insert before", AutoSize = true
    };
    private readonly Button _clearStepSelection = new()
    {
        Text = "Clear selection", AutoSize = true
    };
    private readonly Label _stepSelectionInfo = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false
    };
    private readonly Button _removeStep = new() { Text = "Remove step", AutoSize = true };
    private readonly Button _stepUp = new() { Text = "Move up", AutoSize = true };
    private readonly Button _stepDown = new() { Text = "Move down", AutoSize = true };
    private readonly Button _recordSteps = new()
    {
        Text = "Record / insert steps...", AutoSize = true
    };
    private readonly Button _testMacro = new()
    {
        Text = "Test macro...", Width = 115, Height = 32
    };
    private readonly NumericUpDown _keyDownDelay = new()
    {
        Minimum = 0, Maximum = 60_000, Width = 65, Value = 25
    };
    private readonly NumericUpDown _keyUpDelay = new()
    {
        Minimum = 0, Maximum = 60_000, Width = 65, Value = 25
    };
    private readonly Button _applyKeyDownDelay = new()
    {
        Text = "Apply key-down", AutoSize = true
    };
    private readonly Button _applyKeyUpDelay = new()
    {
        Text = "Apply key-up", AutoSize = true
    };
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
            RowCount = 8, ColumnCount = 1
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
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
        _steps.SelectionChanged += (_, _) => UpdateSelectionInfo();
        _steps.CurrentCellChanged += (_, _) => UpdateSelectionInfo();
        _steps.RowsAdded += (_, _) => NumberStepRows();
        _steps.RowsRemoved += (_, _) => NumberStepRows();
        right.Controls.Add(_steps, 0, 4);

        var stepButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true
        };
        stepButtons.Controls.AddRange(new Control[]
        {
            _addStep, _insertBefore, _removeStep, _stepUp,
            _stepDown, _clearStepSelection, _recordSteps
        });
        right.Controls.Add(stepButtons, 0, 5);

        var timingBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false
        };
        timingBar.Controls.AddRange(new Control[]
        {
            new Label
            {
                Text = "After KeyDown (ms):", AutoSize = true,
                Padding = new Padding(0, 7, 2, 0)
            },
            _keyDownDelay, _applyKeyDownDelay,
            new Label
            {
                Text = "After KeyUp (ms):", AutoSize = true,
                Padding = new Padding(10, 7, 2, 0)
            },
            _keyUpDelay, _applyKeyUpDelay
        });
        right.Controls.Add(timingBar, 0, 6);
        var selectionHelp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        selectionHelp.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        selectionHelp.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selectionHelp.Controls.Add(_stepSelectionInfo, 0, 0);
        selectionHelp.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "No selection: Add/Record append; Insert before prepends. " +
                "Select a numbered row to insert relative to it. " +
                "25ms after KeyDown + 25ms after KeyUp ≈ 50ms per key cycle."
        }, 0, 1);
        right.Controls.Add(selectionHelp, 0, 7);
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
        bottom.Controls.AddRange(new Control[] { save, cancel, _testMacro });
        root.Controls.Add(bottom, 0, 1);
        root.SetColumnSpan(bottom, 2);
        CancelButton = cancel;

        _list.SelectedIndexChanged += (_, _) => SelectionChanged();
        _mode.SelectedIndexChanged += (_, _) => UpdateMode();
        create.Click += (_, _) => AddMacro();
        delete.Click += (_, _) => DeleteMacro();
        _addStep.Click += (_, _) => InsertManualStep(before: false);
        _insertBefore.Click += (_, _) => InsertManualStep(before: true);
        _clearStepSelection.Click += (_, _) => ClearStepSelection();
        _removeStep.Click += (_, _) =>
        {
            if (_steps.CurrentRow is not null)
                _steps.Rows.Remove(_steps.CurrentRow);
        };
        _recordSteps.Click += (_, _) => RecordSteps();
        _testMacro.Click += (_, _) => TestMacro();
        _stepUp.Click += (_, _) => MoveStep(-1);
        _stepDown.Click += (_, _) => MoveStep(+1);
        _applyKeyDownDelay.Click += (_, _) =>
            ApplyTiming(MacroStepKind.KeyDown, (int)_keyDownDelay.Value);
        _applyKeyUpDelay.Click += (_, _) =>
            ApplyTiming(MacroStepKind.KeyUp, (int)_keyUpDelay.Value);
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
            _textDelay.Value = index >= 0
                ? Math.Clamp(_macros[index].TextDelayMs == 0
                    ? 25 : _macros[index].TextDelayMs, 5, 100)
                : 25;
            _steps.Rows.Clear();
            if (index >= 0)
                foreach (MacroStep step in _macros[index].Steps)
                    _steps.Rows.Add(step.Kind.ToString(), step.Parameter,
                        step.DelayAfterMs.ToString());
            // DataGridView selects the first row automatically on load.
            // That is NOT an explicit user selection. Leave it unselected
            // so "Record / insert" appends by default.
            ClearStepSelection();
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
        _addStep.Enabled = _insertBefore.Enabled = _removeStep.Enabled =
            active && !isText;
        _clearStepSelection.Enabled = active && !isText;
        _stepUp.Enabled = _stepDown.Enabled = active && !isText;
        _keyDownDelay.Enabled = _keyUpDelay.Enabled = active && !isText;
        _applyKeyDownDelay.Enabled = _applyKeyUpDelay.Enabled = active && !isText;
        // It is valid to start recording while editing a Text macro.
        // Accepting converts that macro to Steps, without changing its name.
        _recordSteps.Enabled = active;
        _testMacro.Enabled = active;
    }

    /// <summary>
    /// CurrentCell is deliberately null when the user has not selected a
    /// step. Do not use Rows[0] as an implicit selection after a reload.
    /// </summary>
    private int? SelectedStepIndex =>
        _steps.CurrentCell is null ? null : _steps.CurrentCell.RowIndex;

    private void ClearStepSelection()
    {
        _steps.ClearSelection();
        _steps.CurrentCell = null;
        UpdateSelectionInfo();
    }

    private void NumberStepRows()
    {
        for (int i = 0; i < _steps.Rows.Count; i++)
            _steps.Rows[i].HeaderCell.Value = (i + 1).ToString();
        UpdateSelectionInfo();
    }

    private void UpdateSelectionInfo()
    {
        if (_stepSelectionInfo is null) return;
        int? index = SelectedStepIndex;
        _stepSelectionInfo.Text = index.HasValue
            ? $"Selected step {index.Value + 1} of {_steps.Rows.Count} " +
              "— Add/Record after it, or Insert before it."
            : $"{_steps.Rows.Count} step(s); no selection " +
              "— Add/Record append; Insert before adds at the start.";
    }

    private void InsertManualStep(bool before)
    {
        if (_loadedIndex < 0 ||
            _mode.SelectedItem?.ToString() != nameof(MacroMode.Steps))
            return;
        try
        {
            // Capture selection before a possible EndEdit focus change.
            int? selectedIndex = SelectedStepIndex;
            _steps.EndEdit();
            int index = SequenceEditOperations.GetInsertionIndex(
                _steps.Rows.Count, selectedIndex, before);
            _steps.Rows.Insert(index, "KeyPress", "A", "0");
            _steps.CurrentCell = _steps.Rows[index].Cells[0];
            _steps.Rows[index].Selected = true;
            UpdateSelectionInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot add macro step",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void MoveStep(int direction)
    {
        if (_steps.CurrentRow is null) return;
        _steps.EndEdit();
        int index = _steps.CurrentRow.Index;
        int other = index + direction;
        if (other < 0 || other >= _steps.Rows.Count) return;
        // A separate row snapshot avoids storing a DataGridViewRow in two
        // places and preserves the currently edited action and delay values.
        var here = new object?[_steps.Columns.Count];
        var there = new object?[_steps.Columns.Count];
        for (int column = 0; column < _steps.Columns.Count; column++)
        {
            here[column] = _steps.Rows[index].Cells[column].Value;
            there[column] = _steps.Rows[other].Cells[column].Value;
        }
        for (int column = 0; column < _steps.Columns.Count; column++)
        {
            _steps.Rows[index].Cells[column].Value = there[column];
            _steps.Rows[other].Cells[column].Value = here[column];
        }
        _steps.CurrentCell = _steps.Rows[other].Cells[0];
    }

    private void ApplyTiming(MacroStepKind kind, int milliseconds)
    {
        if (_loadedIndex < 0 || _mode.SelectedItem?.ToString() != nameof(MacroMode.Steps))
            return;
        try
        {
            _steps.EndEdit();
            var steps = ReadStepRows();
            int changed = SequenceEditOperations.ApplyKeyboardDelay(
                steps, kind, milliseconds);
            for (int i = 0; i < steps.Count; i++)
                _steps.Rows[i].Cells[2].Value = steps[i].DelayAfterMs.ToString();

            MessageBox.Show(this,
                changed == 0
                    ? $"No {kind} actions to update."
                    : $"Set delay after {changed} {kind} action(s) to {milliseconds} ms. " +
                      "Click Save macros, then Save profile to persist the changes.",
                "Sequence timing", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message,
                "Invalid sequence", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private List<MacroStep> ReadStepRows()
    {
        _steps.EndEdit();
        var steps = new List<MacroStep>();
        foreach (DataGridViewRow row in _steps.Rows)
        {
            var step = new MacroStep
            {
                Kind = Enum.Parse<MacroStepKind>(
                    row.Cells[0].Value?.ToString() ?? "Delay"),
                Parameter = row.Cells[1].Value?.ToString()?.Trim() ?? "",
                DelayAfterMs = int.Parse(row.Cells[2].Value?.ToString() ?? "0")
            };
            step.Validate();
            steps.Add(step);
        }
        return steps;
    }

    private void RecordSteps()
    {
        if (_loadedIndex < 0)
            return;

        // Capture selection BEFORE committing the grid: the validation
        // process can change focus and thus DataGridView.CurrentRow.
        int? selectedIndex = SelectedStepIndex;

        // A newly created Steps macro is allowed to have zero steps here.
        // Validation remains strict when saving a finished macro.
        if (!CommitCurrent(allowEmptySequence: true))
            return;

        using var recorder = new MacroRecorderDialog();
        var response = recorder.ShowDialog(this);
        IReadOnlyList<MacroStep>? recorded = recorder.RecordedSteps;
        if (response != DialogResult.OK || recorded is null || recorded.Count == 0)
            return;

        try
        {
            var macro = _macros[_loadedIndex].Clone();
            macro.Mode = MacroMode.Steps;
            macro.Steps = SequenceEditOperations.InsertRecording(
                macro.Steps, recorded, selectedIndex);
            macro.Validate();

            _macros[_loadedIndex] = macro;
            LoadMacro(_loadedIndex);
            int insertAt = selectedIndex.HasValue
                ? selectedIndex.Value + 1 : macro.Steps.Count - recorded.Count;
            if (insertAt >= 0 && insertAt < _steps.Rows.Count)
                _steps.CurrentCell = _steps.Rows[insertAt].Cells[0];

            MessageBox.Show(this,
                $"Inserted {recorded.Count} step(s) " +
                (selectedIndex.HasValue
                    ? $"after step {selectedIndex.Value + 1}."
                    : "at the end of the macro.") +
                " Existing steps were preserved. You can adjust delays " +
                "before clicking Save macros, then Save profile.",
                "Recording inserted", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not insert recording",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void TestMacro()
    {
        if (_loadedIndex < 0)
            return;

        // The editor's current values, including any unsaved step edits,
        // are used for the preview. This does not save profiles.json and
        // does not require the macro to be assigned to a G-key.
        if (!CommitCurrent())
            return;

        var snapshot = _macros[_loadedIndex].Clone();
        using var dialog = new MacroTestDialog(snapshot);
        dialog.ShowDialog(this);
    }

    private bool CommitCurrent(bool allowEmptySequence = false)
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
            macro.Steps = ReadStepRows();
            macro.Validate(allowEmptySteps: allowEmptySequence);
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
