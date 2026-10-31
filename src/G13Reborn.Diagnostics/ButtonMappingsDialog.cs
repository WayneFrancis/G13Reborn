using G13Reborn.VirtualController;

namespace G13Reborn.Diagnostics;

/// <summary>
/// Editable physical-G13-to-vJoy mapping table.
/// Changes are returned only when Save is pressed, not on Cancel.
/// </summary>
internal sealed class ButtonMappingsDialog : Form
{
    private readonly DataGridView _grid = new();
    public G13ButtonMappings? Result { get; private set; }

    public ButtonMappingsDialog(G13ButtonMappings existing, int? availableButtons)
    {
        Text = "G13 button mappings";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(430, 560);
        Size = new Size(540, 690);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        Controls.Add(layout);

        var instructions = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Choose a vJoy button for each physical G13 control, or None. " +
                "Buttons are forwarded only when 'Forward buttons' is checked.\r\n" +
                (availableButtons is null
                    ? "vJoy is not active. Configure Device 1 with up to 32 buttons in vJoyConf."
                    : $"vJoy device 1 currently exposes {availableButtons} buttons; " +
                      "higher assignments will be ignored until configured."),
            AutoSize = false
        };
        layout.Controls.Add(instructions, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        var nameColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "G13 control", ReadOnly = true, FillWeight = 55
        };
        var bitColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "Input bit", ReadOnly = true, FillWeight = 25
        };
        var mappingColumn = new DataGridViewComboBoxColumn
        {
            HeaderText = "vJoy button", FlatStyle = FlatStyle.Flat, FillWeight = 50
        };
        mappingColumn.Items.Add("None");
        for (int i = 1; i <= G13ButtonMappings.MaxVJoyButton; i++)
            mappingColumn.Items.Add(i.ToString());
        _grid.Columns.AddRange(nameColumn, bitColumn, mappingColumn);

        foreach (var button in G13ButtonMappings.PhysicalButtons)
        {
            int target = existing.GetAssignment(button.Bit);
            int index = _grid.Rows.Add(button.Name, button.Bit,
                target == 0 ? "None" : target.ToString());
            _grid.Rows[index].Tag = button.Bit;
        }
        layout.Controls.Add(_grid, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var save = new Button { Text = "Save mappings", Width = 125, Height = 32 };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 32 };
        var defaults = new Button { Text = "Restore defaults", Width = 125, Height = 32 };
        save.Click += (_, _) => SaveClicked();
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        defaults.Click += (_, _) => ResetDefaults();
        actions.Controls.AddRange(new Control[] { save, cancel, defaults });
        layout.Controls.Add(actions, 0, 2);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void ResetDefaults()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            int bit = (int)row.Tag!;
            int target = G13ButtonMappings.PhysicalButtons
                .First(x => x.Bit == bit).DefaultVJoyButton;
            row.Cells[2].Value = target == 0 ? "None" : target.ToString();
        }
    }

    private void SaveClicked()
    {
        _grid.EndEdit();
        var assignments = new Dictionary<int, int>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            int bit = (int)row.Tag!;
            string selected = row.Cells[2].Value?.ToString() ?? "None";
            assignments[bit] = selected == "None" ? 0 : int.Parse(selected);
        }

        try
        {
            Result = new G13ButtonMappings(assignments);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid button mapping",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
