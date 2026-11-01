using System.Runtime.InteropServices;
using G13Reborn.Mapping;

namespace G13Reborn.Config;

/// <summary>
/// Test one unsaved macro without starting G13 HID forwarding. Requires
/// explicit user action and a different foreground application; releases
/// all preview-held inputs on completion, cancellation and form close.
/// </summary>
internal sealed class MacroTestDialog : Form
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    private readonly MacroDefinition _macro;
    private readonly MacroPreviewRunner _runner =
        new(Win32InputSender.Send, Win32InputSender.SendUnicodeText);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Button _cancel = new()
    {
        Text = "Cancel test", Width = 120, Height = 34
    };
    private DateTime _startsAt;
    private bool _started;
    private bool _finished;
    private bool _closeOnFinish;

    public MacroTestDialog(MacroDefinition macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        macro.Validate();
        _macro = macro.Clone();

        Text = $"Test macro — {_macro.Name}";
        ClientSize = new Size(590, 220);
        MinimumSize = new Size(490, 220);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = $"Testing: {_macro.Name}\r\n" +
                "This will send REAL keyboard and mouse actions to the focused " +
                "application. Use a disposable Notepad document first.\r\n" +
                "Switch to that application during the countdown. " +
                "The test will not start while a G13Reborn window has focus.\r\n" +
                "Click Cancel test to stop and release all preview-held keys."
        }, 0, 0);
        root.Controls.Add(_status, 0, 1);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(_cancel);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        CancelButton = _cancel;

        _cancel.Click += (_, _) => CancelClicked();
        _timer.Tick += (_, _) => CheckCountdown();
        Shown += (_, _) =>
        {
            _startsAt = DateTime.UtcNow.AddSeconds(3);
            _timer.Start();
            CheckCountdown();
        };
    }

    private bool HasExternalForeground()
    {
        nint window = GetForegroundWindow();
        if (window == 0) return false;
        GetWindowThreadProcessId(window, out uint pid);
        return pid != 0 && pid != (uint)Environment.ProcessId;
    }

    private void CheckCountdown()
    {
        if (_started || _finished) return;
        double seconds = (_startsAt - DateTime.UtcNow).TotalSeconds;
        if (seconds > 0)
        {
            _status.Text =
                $"Starts in {Math.Ceiling(seconds):F0} seconds — focus the test application.";
            return;
        }

        if (!HasExternalForeground())
        {
            _status.Text = "Ready. Focus another application (e.g. Notepad) to begin.";
            return;
        }

        _started = true;
        _timer.Stop();
        _status.Text = "Playing macro... Click Cancel test to stop.";
        _ = RunTestAsync();
    }

    private async Task RunTestAsync()
    {
        // A preview cannot run without limit if its recorded delays are huge.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        string result;
        try
        {
            await Task.Run(() => _runner.RunAsync(_macro, timeout.Token));
            result = "Macro completed. All preview-held keys/buttons released.";
        }
        catch (OperationCanceledException)
        {
            result = timeout.IsCancellationRequested
                ? "Test stopped after the 2-minute safety limit."
                : "Macro test cancelled. All preview-held keys/buttons released.";
        }
        catch (Exception ex)
        {
            result = $"Macro test failed: {ex.Message}";
        }

        // Task completion means the runner has finished its finally block and
        // has attempted to release every held keyboard/mouse control.
        _finished = true;
        if (IsDisposed || Disposing) return;
        _status.Text = result;
        _cancel.Enabled = true;
        _cancel.Text = "Close";
        if (_closeOnFinish) Close();
    }

    private void CancelClicked()
    {
        if (!_started || _finished)
        {
            Close();
            return;
        }

        _closeOnFinish = true;
        _cancel.Enabled = false;
        _status.Text = "Cancelling and releasing preview input...";
        _runner.Cancel();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_started && !_finished)
        {
            // Do not destroy the modal window until the playback worker has
            // exited and completed input cleanup. A close attempt also stops
            // future macro actions immediately.
            e.Cancel = true;
            CancelClicked();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
