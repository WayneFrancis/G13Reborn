namespace G13Reborn.Config;

internal static class K8850MacroDialog
{
    public static string? Edit(Form owner, string? existing, IEnumerable<string> availableKeys)
    {
        K8850MacroSequence sequence;
        try { sequence = K8850MacroSequence.Parse(existing); }
        catch (Exception ex)
        {
            if (MessageBox.Show(owner, "The existing assignment cannot be edited as a named keyboard sequence. " +
                "Start a new sequence instead? The original will be retained if you cancel.\r\n\r\n" + ex.Message,
                "Replace assignment?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return null;
            sequence = new();
        }

        using var dialog = new Form { Text = "Edit hardware keyboard macro", Size = new Size(760, 565),
            MinimumSize = new Size(650, 460), StartPosition = FormStartPosition.CenterParent };
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
            MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        foreach (string column in new[] { "Step", "Keyboard shortcut", "Hardware tokens" })
            grid.Columns.Add(column, column);
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.Columns[0].FillWeight = 22;
        grid.Columns[2].FillWeight = 40;

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 120, Padding = new Padding(8),
            FlowDirection = FlowDirection.TopDown, WrapContents = false };
        header.Controls.Add(new Label { AutoSize = true, Text = "Choose a key and modifiers, then add, insert or replace a step." });
        var editor = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var key = new ComboBox { Width = 205, DropDownStyle = ComboBoxStyle.DropDown };
        key.Items.AddRange(availableKeys.Where(k => !k.Contains('+') && !k.Contains(','))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        key.Text = "A";
        editor.Controls.Add(key);
        var capture = new Button { Text = "Capture shortcut...", AutoSize = true };
        editor.Controls.Add(capture);
        header.Controls.Add(editor);
        var modifiers = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var checks = new[] { "Ctrl", "Shift", "Alt", "Win", "RCtrl", "RShift", "RAlt", "RWin" }
            .Select(m => new CheckBox { Text = m, AutoSize = true }).ToArray();
        modifiers.Controls.AddRange(checks);
        header.Controls.Add(modifiers);
        var budget = new Label { AutoSize = true };
        header.Controls.Add(budget);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 95, Padding = new Padding(8), WrapContents = true };
        Button AddButton(string title, Action action)
        {
            var button = new Button { Text = title, AutoSize = true };
            button.Click += (_, _) => {
                try { action(); }
                catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "Macro editor"); }
            };
            footer.Controls.Add(button);
            return button;
        }
        int Selected() => grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Index : -1;
        string Step() => string.Join("+", checks.Where(c => c.Checked).Select(c => c.Text).Append(key.Text.Trim()));
        bool refreshing = false;
        void ShowStep(string step)
        {
            var parts = step.Split('+', StringSplitOptions.TrimEntries);
            foreach (var check in checks)
                check.Checked = parts.Take(parts.Length - 1).Contains(check.Text, StringComparer.OrdinalIgnoreCase);
            key.Text = parts[^1];
        }
        Button? apply = null;
        void Refresh(int index)
        {
            refreshing = true;
            try
            {
                grid.Rows.Clear();
                for (int i = 0; i < sequence.Steps.Count; i++)
                    grid.Rows.Add(i + 1, sequence.Steps[i], K8850MacroSequence.CountStep(sequence.Steps[i]));
                grid.ClearSelection();
                if (index >= 0 && index < grid.Rows.Count)
                {
                    grid.CurrentCell = grid.Rows[index].Cells[0];
                    grid.Rows[index].Selected = true;
                    ShowStep(sequence.Steps[index]);
                }
                budget.Text = $"Hardware tokens: {sequence.TokenCount} / {K8850MacroSequence.MaxTokens} — modifiers consume one token each.";
                if (apply != null) apply.Enabled = sequence.Steps.Count > 0;
            }
            finally { refreshing = false; }
        }
        grid.SelectionChanged += (_, _) => {
            if (!refreshing && Selected() is int index && index >= 0) ShowStep(sequence.Steps[index]);
        };
        AddButton("Add", () => { int index = sequence.Steps.Count; sequence.Insert(index, Step()); Refresh(index); });
        AddButton("Insert before", () => { int index = Math.Max(0, Selected()); sequence.Insert(index, Step()); Refresh(index); });
        AddButton("Replace", () => { int index = Selected(); if (index >= 0) { sequence.Replace(index, Step()); Refresh(index); } });
        AddButton("Duplicate", () => { int index = Selected(); if (index >= 0) { sequence.Insert(index + 1, sequence.Steps[index]); Refresh(index + 1); } });
        AddButton("Delete", () => { int index = Selected(); if (index >= 0) { sequence.Remove(index); Refresh(Math.Min(index, sequence.Steps.Count - 1)); } });
        AddButton("Up", () => { int index = Selected(); if (index > 0) { sequence.Move(index, index - 1); Refresh(index - 1); } });
        AddButton("Down", () => { int index = Selected(); if (index >= 0 && index < sequence.Steps.Count - 1) { sequence.Move(index, index + 1); Refresh(index + 1); } });
        apply = AddButton("Apply locally", () => {
            if (sequence.Steps.Count == 0) return;
            K8850Binding.Encode(1, 1, sequence.Binding);
            dialog.DialogResult = DialogResult.OK;
        });
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(cancel);
        dialog.CancelButton = cancel;
        capture.Click += (_, _) =>
        {
            using var window = new Form { Text = "Capture shortcut", Size = new Size(410, 140),
                StartPosition = FormStartPosition.CenterParent, KeyPreview = true };
            window.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Text = "Press a supported keyboard shortcut. Close to cancel." });
            window.KeyDown += (_, e) => {
                e.Handled = e.SuppressKeyPress = true;
                if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
                string capturedKey = e.KeyCode switch {
                    >= Keys.D0 and <= Keys.D9 => ((int)e.KeyCode - (int)Keys.D0).ToString(),
                    >= Keys.NumPad0 and <= Keys.NumPad9 => "Numpad " + ((int)e.KeyCode - (int)Keys.NumPad0),
                    Keys.Return => "Enter", Keys.Prior => "PageUp", Keys.Next => "PageDown",
                    Keys.Divide => "Numpad Divide", Keys.Multiply => "Numpad Multiply",
                    Keys.Subtract => "Numpad Subtract", Keys.Add => "Numpad Add", Keys.Decimal => "Numpad Decimal",
                    _ => e.KeyCode.ToString()
                };
                string chord = (e.Control ? "Ctrl+" : "") + (e.Shift ? "Shift+" : "") + (e.Alt ? "Alt+" : "") + capturedKey;
                try { K8850MacroSequence.CountStep(chord); }
                catch (Exception ex) { MessageBox.Show(window, ex.Message); return; }
                ShowStep(chord);
                window.DialogResult = DialogResult.OK;
            };
            window.ShowDialog(dialog);
        };
        dialog.Controls.Add(grid);
        dialog.Controls.Add(footer);
        dialog.Controls.Add(header);
        Refresh(sequence.Steps.Count > 0 ? 0 : -1);
        return dialog.ShowDialog(owner) == DialogResult.OK ? sequence.Binding : null;
    }
}
