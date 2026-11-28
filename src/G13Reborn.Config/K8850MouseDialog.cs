namespace G13Reborn.Config;

internal static class K8850MouseDialog
{
    public static string? Edit(Form owner, string? existing)
    {
        var initial = new K8850MouseAction(1, 0, 0, 0, 0);
        try
        {
            if (!string.IsNullOrWhiteSpace(existing) && K8850MouseAction.TryParse(existing, out var parsed))
                initial = parsed;
        }
        catch (Exception ex) { MessageBox.Show(owner, "Existing mouse action is invalid: " + ex.Message); }
        using var dialog = new Form { Text = "Configure hardware mouse action", Size = new Size(550, 410),
            StartPosition = FormStartPosition.CenterParent };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        dialog.Controls.Add(panel);
        var action = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        action.Items.AddRange(new object[] { "Left click", "Right click", "Middle click", "Relative movement", "Scroll wheel" });
        action.SelectedIndex = initial.Buttons switch { 1 => 0, 2 => 1, 4 => 2, _ => initial.Wheel != 0 ? 4 : 3 };
        var modifier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        modifier.Items.AddRange(new object[] { "None", "Ctrl", "Shift", "Alt" });
        modifier.SelectedIndex = initial.Modifier switch { 0xf1 => 1, 0xf2 => 2, 0xf3 => 3, _ => 0 };
        NumericUpDown Number(int value) => new() { Minimum = -128, Maximum = 127, Value = value, Width = 75 };
        var x = Number(initial.X), y = Number(initial.Y), wheel = Number(initial.Wheel == 0 ? 1 : initial.Wheel);
        var movement = new FlowLayoutPanel { AutoSize = true };
        movement.Controls.AddRange(new Control[] { new Label { Text = "X", AutoSize = true }, x,
            new Label { Text = "Y", AutoSize = true }, y });
        var scrolling = new FlowLayoutPanel { AutoSize = true };
        scrolling.Controls.AddRange(new Control[] { new Label { Text = "Wheel delta", AutoSize = true }, wheel });
        var preview = new Label { AutoSize = true, MaximumSize = new Size(490, 0) };
        var apply = new Button { Text = "Apply locally", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        K8850MouseAction Current()
        {
            byte m = modifier.SelectedIndex == 0 ? (byte)0 : (byte)(0xf0 + modifier.SelectedIndex);
            return action.SelectedIndex switch {
                0 => new(1, 0, 0, 0, m), 1 => new(2, 0, 0, 0, m), 2 => new(4, 0, 0, 0, m),
                3 => new(0, (sbyte)x.Value, (sbyte)y.Value, 0, m), _ => new(0, 0, 0, (sbyte)wheel.Value, m)
            };
        }
        void Refresh()
        {
            movement.Enabled = action.SelectedIndex == 3;
            scrolling.Enabled = action.SelectedIndex == 4;
            try
            {
                var current = Current();
                current.Encode(1, 1);
                preview.Text = "Assignment: " + current.Binding;
                apply.Enabled = true;
            }
            catch (Exception ex) { preview.Text = ex.Message; apply.Enabled = false; }
        }
        action.SelectedIndexChanged += (_, _) => Refresh();
        modifier.SelectedIndexChanged += (_, _) => Refresh();
        x.ValueChanged += (_, _) => Refresh();
        y.ValueChanged += (_, _) => Refresh();
        wheel.ValueChanged += (_, _) => Refresh();
        panel.Controls.AddRange(new Control[] { new Label { Text = "Action", AutoSize = true }, action,
            new Label { Text = "Optional modifier", AutoSize = true }, modifier, movement, scrolling,
            new Label { Text = "Positive X = right, positive Y = down, positive wheel = up.\r\nRelative HID units, not guaranteed screen pixels. New actions require hardware testing.",
                AutoSize = true, MaximumSize = new Size(490, 0) }, preview, apply, cancel });
        string? result = null;
        apply.Click += (_, _) => {
            var current = Current();
            current.Encode(1, 1);
            result = current.Binding;
            dialog.DialogResult = DialogResult.OK;
        };
        dialog.CancelButton = cancel;
        Refresh();
        return dialog.ShowDialog(owner) == DialogResult.OK ? result : null;
    }
}
