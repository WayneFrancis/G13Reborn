using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using G13Reborn.Mapping;

namespace G13Reborn.Config;

/// <summary>
/// Explicit, read-only Windows Raw Input diagnostic for one selected key (K).
/// Listens only after clicking Start. Displays origin device information and
/// key edges; it never intercepts, remaps, injects, or saves keystrokes.
/// RIDEV_INPUTSINK allows a test with Notepad as the foreground application.
/// </summary>
internal sealed class RawInputDiagnosticDialog : Form
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const uint RimTypeKeyboard = 1;
    private const ushort RiKeyBreak = 1;
    private const ushort MonitoredVirtualKey = 0x4B; // K

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint TargetWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices, uint count, uint structureSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        nint rawInput, uint command, nint data, ref uint size, uint headerSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceName(
        nint device, uint command, StringBuilder? output, ref uint size);

    private readonly DataGridView _events = new();
    private readonly Button _start = new() { Text = "Start capture", AutoSize = true };
    private readonly Button _stop = new() { Text = "Stop capture", AutoSize = true, Enabled = false };
    private readonly Button _clear = new() { Text = "Clear", AutoSize = true };
    private readonly Button _copy = new() { Text = "Copy results", AutoSize = true };
    private readonly Label _status = new()
    {
        AutoSize = true,
        Text = "Stopped. Click Start capture to observe K key presses/releases."
    };
    private readonly Dictionary<nint, string> _deviceNames = new();
    private bool _listening;
    private int _eventCount;

    public RawInputDiagnosticDialog()
    {
        Text = "G13Reborn — Read-only Raw Input source diagnostic";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 480);
        Size = new Size(1150, 590);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        var instructions = new Label
        {
            Dock = DockStyle.Fill,
            Text =
                "This diagnostic observes only the K key using Windows Raw Input. " +
                "Nothing is injected or saved, and other key values are discarded.\r\n" +
                "Click Start capture, then type K using your physical keyboard and " +
                "the G13 assigned to K by LGS. Notepad may have focus: capture still " +
                "receives background keyboard input.\r\n" +
                "Raw Input identifies a HID keyboard device, but an absent event " +
                "does not establish which software generated an ordinary Windows key. " +
                "Full device paths may contain machine-specific identifiers.",
            AutoSize = false
        };
        root.Controls.Add(instructions, 0, 0);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false
        };
        controls.Controls.AddRange(new Control[]
        {
            _start, _stop, _clear, _copy
        });
        root.Controls.Add(controls, 0, 1);

        _events.Dock = DockStyle.Fill;
        _events.ReadOnly = true;
        _events.AllowUserToAddRows = false;
        _events.AllowUserToDeleteRows = false;
        _events.AllowUserToResizeRows = false;
        _events.RowHeadersVisible = false;
        _events.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _events.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _events.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Time", FillWeight = 19
        });
        _events.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Key", FillWeight = 9
        });
        _events.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Edge", FillWeight = 11
        });
        _events.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Device category", FillWeight = 48
        });
        _events.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Raw Input device path", FillWeight = 145
        });
        root.Controls.Add(_events, 0, 2);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false
        };
        var close = new Button { Text = "Close", AutoSize = true };
        bottom.Controls.Add(_status);
        bottom.Controls.Add(close);
        root.Controls.Add(bottom, 0, 3);

        _start.Click += (_, _) => StartCapture();
        _stop.Click += (_, _) => StopCapture();
        _clear.Click += (_, _) =>
        {
            _events.Rows.Clear();
            _eventCount = 0;
            _status.Text = _listening ? "Listening for K." : "Stopped.";
        };
        _copy.Click += (_, _) => CopyResults();
        close.Click += (_, _) => Close();
    }

    private static RawInputDevice KeyboardRegistration(uint flags, nint window) => new()
    {
        UsagePage = 0x01, Usage = 0x06,
        Flags = flags, TargetWindow = window
    };

    private void StartCapture()
    {
        if (_listening) return;
        try
        {
            var devices = new[]
            {
                KeyboardRegistration(RidevInputSink, Handle)
            };
            if (!RegisterRawInputDevices(devices, 1,
                (uint)Marshal.SizeOf<RawInputDevice>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Could not register for read-only keyboard Raw Input.");
            _listening = true;
            _start.Enabled = false;
            _stop.Enabled = true;
            _status.Text = "Listening for K (including while another app has focus).";
        }
        catch (Exception ex)
        {
            _status.Text = "Raw Input registration failed: " + ex.Message;
        }
    }

    private void StopCapture()
    {
        if (!_listening) return;
        // RIDEV_REMOVE requires a null target HWND.
        var devices = new[]
        {
            KeyboardRegistration(RidevRemove, nint.Zero)
        };
        bool removed = RegisterRawInputDevices(devices, 1,
            (uint)Marshal.SizeOf<RawInputDevice>());
        _listening = false;
        _start.Enabled = true;
        _stop.Enabled = false;
        _status.Text = removed
            ? $"Stopped. Captured {_eventCount} K press/release event(s)."
            : "Raw Input removal failed: " +
              new Win32Exception(Marshal.GetLastWin32Error()).Message;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopCapture();
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmInput && _listening)
        {
            try { ProcessRawInput(m.LParam); }
            catch (Exception ex)
            {
                // A malformed device report must never break the UI loop.
                _status.Text = "Raw Input read failed: " + ex.Message;
            }
        }
        // Foreground WM_INPUT needs the default window procedure cleanup.
        base.WndProc(ref m);
    }

    private void ProcessRawInput(nint rawHandle)
    {
        uint headerLength = (uint)Marshal.SizeOf<RawInputHeader>();
        uint length = 0;
        uint query = GetRawInputData(rawHandle, RidInput,
            nint.Zero, ref length, headerLength);
        if (query == uint.MaxValue ||
            length < headerLength + (uint)Marshal.SizeOf<RawKeyboard>() ||
            length > 64 * 1024)
            return;

        nint memory = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            uint available = length;
            uint copied = GetRawInputData(rawHandle, RidInput,
                memory, ref available, headerLength);
            if (copied == uint.MaxValue || copied < headerLength +
                (uint)Marshal.SizeOf<RawKeyboard>())
                return;

            var header = Marshal.PtrToStructure<RawInputHeader>(memory);
            if (header.Type != RimTypeKeyboard)
                return;

            var keyboard = Marshal.PtrToStructure<RawKeyboard>(
                IntPtr.Add(memory, (int)headerLength));
            if (keyboard.VirtualKey != MonitoredVirtualKey)
                return;

            bool released = (keyboard.Flags & RiKeyBreak) != 0;
            string deviceName = ResolveDeviceName(header.Device);
            int index = _events.Rows.Add(
                DateTime.Now.ToString("HH:mm:ss.fff"),
                "K", released ? "Up" : "Down",
                RawKeyboardDeviceLabel.Describe(deviceName),
                deviceName);
            if (_events.Rows.Count > 300)
                _events.Rows.RemoveAt(0);
            else
                _events.FirstDisplayedScrollingRowIndex = index;
            _eventCount++;
            _status.Text = $"Listening: {_eventCount} K event(s), " +
                $"{_deviceNames.Count} device(s) observed. No data saved.";
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private string ResolveDeviceName(nint handle)
    {
        if (_deviceNames.TryGetValue(handle, out string? previous))
            return previous;

        string name;
        if (handle == nint.Zero)
        {
            name = "(no device handle)";
        }
        else
        {
            uint chars = 0;
            uint query = GetRawInputDeviceName(handle,
                RidiDeviceName, null, ref chars);
            if (query == uint.MaxValue || chars == 0 || chars > 4096)
            {
                name = $"(device handle 0x{handle:X})";
            }
            else
            {
                var buffer = new StringBuilder((int)chars);
                uint returned = GetRawInputDeviceName(handle,
                    RidiDeviceName, buffer, ref chars);
                name = returned == uint.MaxValue
                    ? $"(unavailable device 0x{handle:X})"
                    : buffer.ToString();
            }
        }
        _deviceNames[handle] = name;
        return name;
    }

    private void CopyResults()
    {
        if (_events.Rows.Count == 0)
        {
            _status.Text = "No K events to copy.";
            return;
        }
        var lines = new List<string>
        {
            "Time\tKey\tEdge\tDevice category\tRaw Input device path"
        };
        foreach (DataGridViewRow row in _events.Rows)
            lines.Add(string.Join('\t', row.Cells.Cast<DataGridViewCell>()
                .Select(x => x.Value?.ToString() ?? "")));
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            _status.Text = "Copied displayed K events. " +
                "Review device paths before sharing.";
        }
        catch (Exception ex)
        {
            _status.Text = "Copy failed: " + ex.Message;
        }
    }
}
