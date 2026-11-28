using G13Reborn.Device.Hid;

namespace G13Reborn.Config;

internal static class K8850Diagnostics
{
    public static void Show(Form owner, K8850Device.Interface? device)
    {
        using var dialog = new Form { Text = "K8850 readback diagnostics", Size = new Size(900, 580),
            StartPosition = FormStartPosition.CenterParent };
        var output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 10) };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
        var scan = new Button { Text = "Inspect HID interfaces", AutoSize = true };
        var listen = new Button { Text = "Listen for 15 seconds", AutoSize = true, Enabled = device != null };
        var stop = new Button { Text = "Stop", AutoSize = true, Enabled = false };
        var save = new Button { Text = "Save log...", AutoSize = true };
        var close = new Button { Text = "Close", AutoSize = true };
        footer.Controls.AddRange(new Control[] { scan, listen, stop, save, close });
        var hint = new Label { Dock = DockStyle.Top, Height = 58, Text =
            "Read-only metadata and passive vendor-interface reports. No commands are sent. " +
            "A silent device does not prove readback is unsupported. Logs may contain device instance identifiers or stored shortcuts; review before sharing." };
        dialog.Controls.Add(output);
        dialog.Controls.Add(footer);
        dialog.Controls.Add(hint);

        CancellationTokenSource? active = null;
        void Log(string line) => output.AppendText(line + Environment.NewLine);
        void Inspect()
        {
            Log("=== HID metadata " + DateTimeOffset.Now.ToString("O") + " ===");
            try
            {
                var interfaces = K8850Device.DescribeInterfaces();
                Log($"Matching interfaces: {interfaces.Count}");
                foreach (var item in interfaces)
                {
                    Log($"Usage page={item.UsagePage:X4}, usage={item.Usage:X4}, input={item.InputLength}, output={item.OutputLength}, feature={item.FeatureLength}, revision={item.Version?.ToString("X4") ?? "unknown"}");
                    Log(item.Path);
                }
            }
            catch (Exception ex) { Log("Metadata error: " + ex.Message); }
        }
        scan.Click += (_, _) => Inspect();
        listen.Click += async (_, _) =>
        {
            if (device == null || active != null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            active = timeout;
            listen.Enabled = scan.Enabled = false;
            stop.Enabled = true;
            int received = 0;
            var reports = new System.Collections.Concurrent.ConcurrentQueue<string>();
            Log("=== Passive vendor listening (no writes) ===");
            try
            {
                await K8850Device.ListenAsync(device, report =>
                {
                    reports.Enqueue($"{DateTimeOffset.Now:O} IN ({report.Length}): {Convert.ToHexString(report)}");
                }, timeout.Token);
            }
            catch (OperationCanceledException) { Log("Listener stopped or timed out."); }
            catch (Exception ex) { Log("Listener error: " + ex.Message); }
            finally
            {
                while (reports.TryDequeue(out var line)) { Log(line); received++; }
                Log($"Received {received} report(s). No configuration commands sent.");
                active = null;
                listen.Enabled = scan.Enabled = true;
                stop.Enabled = false;
            }
        };
        stop.Click += (_, _) => active?.Cancel();
        close.Click += (_, _) => dialog.Close();
        dialog.FormClosing += (_, e) =>
        {
            if (active == null) return;
            active.Cancel();
            e.Cancel = true;
        };
        save.Click += (_, _) =>
        {
            using var picker = new SaveFileDialog { Filter = "Text log (*.txt)|*.txt",
                FileName = "k8850-diagnostics.txt", AddExtension = true };
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            try { File.WriteAllText(picker.FileName, output.Text); }
            catch (Exception ex) { MessageBox.Show(dialog, ex.Message); }
        };
        Inspect();
        dialog.ShowDialog(owner);
    }
}
