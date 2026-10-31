using System.Diagnostics;
using System.Runtime.InteropServices;
using G13Reborn.Mapping;

namespace G13Reborn.Config;

/// <summary>
/// Explicit, visible, time-limited keyboard/mouse recording. Uses read-only
/// WH_KEYBOARD_LL and WH_MOUSE_LL hooks; it never suppresses system input.
/// Hooks exist only while this modal recorder is visibly active.
/// </summary>
internal sealed class MacroRecorderDialog : Form
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const uint LlKeyboardInjected = 0x10;
    private const uint LlMouseInjected = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHook
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Timestamp;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHook
    {
        public Point Position;
        public uint MouseData;
        public uint Flags;
        public uint Timestamp;
        public nuint ExtraInfo;
    }

    private delegate nint HookCallback(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    private static extern nint SetWindowsHookEx(
        int type, HookCallback callback, nint module, uint threadId);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(
        nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    private readonly HookCallback _keyboardCallback;
    private readonly HookCallback _mouseCallback;
    private readonly MacroRecordingSession _session = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Button _use = new()
    {
        Text = "Stop and use recording", AutoSize = true
    };
    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _isRecording;
    private bool _ending;

    public IReadOnlyList<MacroStep>? RecordedSteps { get; private set; }

    public MacroRecorderDialog()
    {
        Text = "Record sequence — G13Reborn";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(625, 235);
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        _keyboardCallback = OnKeyboard;
        _mouseCallback = OnMouse;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(14),
            ColumnCount = 1, RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Recording keyboard presses/releases, mouse buttons and wheel in other " +
                "applications. Focus the target application and perform your actions.\r\n" +
                "Mouse movement and pointer coordinates are NOT recorded. " +
                "Do not type passwords or other sensitive information while recording.\r\n" +
                "Maximum 60 seconds / 200 actions. Only accepted recordings are saved."
        }, 0, 0);
        layout.Controls.Add(_status, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var cancel = new Button
        {
            Text = "Discard", AutoSize = true,
            DialogResult = DialogResult.Cancel
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_use);
        layout.Controls.Add(buttons, 0, 2);
        CancelButton = cancel;

        _use.Click += (_, _) => CompleteRecording();
        _timer.Tick += (_, _) => RefreshStatus();
        Shown += (_, _) =>
        {
            try { BeginRecording(); }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not install the temporary keyboard/mouse recording hooks.\r\n" +
                    ex.Message, "Macro recorder",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    private void BeginRecording()
    {
        // Low-level hook callbacks execute on the thread installing the hook,
        // which is the WinForms UI thread here. No background message loop
        // or cross-thread UI access is required.
        nint module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardCallback, module, 0);
        if (_keyboardHook == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseCallback, module, 0);
        if (_mouseHook == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        _stopwatch.Start();
        _isRecording = true;
        _timer.Start();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        _status.Text =
            $"Recording: {_stopwatch.Elapsed.TotalSeconds:F1}s / 60s   " +
            $"Steps: {_session.StepCount} / {MacroRecordingSession.MaximumSteps}";
        if (_isRecording &&
            (_stopwatch.ElapsedMilliseconds >= MacroRecordingSession.MaximumDurationMs ||
             _session.AtLimit))
        {
            CompleteRecording();
        }
    }

    private bool OtherApplicationHasFocus()
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0) return false;
        GetWindowThreadProcessId(foreground, out uint processId);
        // Exclude our own recorder/editor controls (notably the Stop click).
        return processId != 0 && processId != (uint)Environment.ProcessId;
    }

    private nint OnKeyboard(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code >= 0 && _isRecording && OtherApplicationHasFocus())
            {
                int message = unchecked((int)(long)wParam);
                if (message is WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp)
                {
                    KeyboardHook data = Marshal.PtrToStructure<KeyboardHook>(lParam);
                    if ((data.Flags & LlKeyboardInjected) == 0)
                        _session.RecordKeyboard(data.VkCode,
                            message is WmKeyDown or WmSysKeyDown,
                            _stopwatch.ElapsedMilliseconds);
                }
            }
        }
        catch (Exception) { /* never throw from an unmanaged hook callback */ }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code >= 0 && _isRecording && OtherApplicationHasFocus())
            {
                int message = unchecked((int)(long)wParam);
                MouseHook data = Marshal.PtrToStructure<MouseHook>(lParam);
                if ((data.Flags & LlMouseInjected) == 0)
                {
                    long elapsed = _stopwatch.ElapsedMilliseconds;
                    switch (message)
                    {
                        case WmLButtonDown: case WmLButtonUp:
                            _session.RecordMouseButton("Left",
                                message == WmLButtonDown, elapsed);
                            break;
                        case WmRButtonDown: case WmRButtonUp:
                            _session.RecordMouseButton("Right",
                                message == WmRButtonDown, elapsed);
                            break;
                        case WmMButtonDown: case WmMButtonUp:
                            _session.RecordMouseButton("Middle",
                                message == WmMButtonDown, elapsed);
                            break;
                        case WmMouseWheel:
                            int delta = (short)(data.MouseData >> 16);
                            _session.RecordWheel(delta, elapsed);
                            break;
                    }
                }
            }
        }
        catch (Exception) { /* never throw from an unmanaged hook callback */ }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private void CompleteRecording()
    {
        if (_ending) return;
        _ending = true;
        StopHooks();
        var steps = _session.Finish(_stopwatch.ElapsedMilliseconds);
        if (steps.Count == 0)
        {
            MessageBox.Show(this,
                "No recordable input was detected. Focus another application " +
                "while recording and try again.", "Empty macro recording");
            DialogResult = DialogResult.Cancel;
        }
        else
        {
            RecordedSteps = steps;
            DialogResult = DialogResult.OK;
        }
        Close();
    }

    private void StopHooks()
    {
        _isRecording = false;
        _timer.Stop();
        _stopwatch.Stop();
        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopHooks();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
