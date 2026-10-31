using System.Collections.Concurrent;
using System.Text;
using G13Reborn.Device;
using G13Reborn.Device.Hid;
using G13Reborn.VirtualController;

namespace G13Reborn.Diagnostics;

/// <summary>Read-only diagnostics: HID enumeration, live reports and optional raw capture.</summary>
public sealed class MainForm : Form
{
    private const int MaxRecentReports = 350;
    private const int MaxQueuedReports = 2_000;

    private readonly ListView _devices = new();
    private readonly Button _connect = new() { Text = "Connect (read only)", AutoSize = true };
    private readonly Button _disconnect = new() { Text = "Disconnect", AutoSize = true };
    private readonly Button _startCapture = new() { Text = "Start capture...", AutoSize = true };
    private readonly Button _stopCapture = new() { Text = "Stop capture", AutoSize = true };
    private readonly Label _status = new();
    private readonly Label _axes = new();
    private readonly Label _buttons = new();
    private readonly Label _rawButtons = new();
    private readonly Label _dropped = new();
    private readonly Label _auxButtons = new();
    private readonly Label _vJoyStatus = new() { AutoSize = true, Text = "vJoy: off.", TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _startVJoy = new() { Text = "Enable vJoy", AutoSize = true };
    private readonly Button _stopVJoy = new() { Text = "Disable vJoy", AutoSize = true };
    private readonly NumericUpDown _deadzone = new() { Minimum = 0, Maximum = 30, Value = 10, Width = 50 };
    private readonly CheckBox _invertY = new() { Text = "Invert Y", AutoSize = true };
    private readonly object _virtualGate = new();
    private readonly JoystickView _joystick = new();
    private readonly ListBox _recent = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 50 };
    private readonly ConcurrentQueue<Sample> _incoming = new();
    private readonly object _captureGate = new();

    private int _pendingCount;
    private int _droppedCount;
    private int _deadzonePercent = 10;
    private int _invertYAxis;
    private int _vJoyErrorPending;
    private VJoyDevice? _vJoy;
    private StreamWriter? _captureWriter;
    private G13InputReader? _reader;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;

    private sealed record Sample(DateTimeOffset Timestamp, byte[] Bytes);

    public MainForm()
    {
        Text = "G13Reborn - USB / Analog Diagnostics";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 760);
        MinimumSize = new Size(850, 600);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 165));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 195));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var refresh = new Button { Text = "Refresh devices", AutoSize = true };
        refresh.Click += (_, _) => RefreshDevices();
        _connect.Click += ConnectClicked;
        _disconnect.Click += async (_, _) => await StopReaderAsync();
        _startCapture.Click += StartCaptureClicked;
        _stopCapture.Click += (_, _) => StopCapture();
        actions.Controls.AddRange(
            new Control[] { refresh, _connect, _disconnect, _startCapture, _stopCapture });
        root.Controls.Add(actions, 0, 0);

        // Optional software-only output; no driver installation or HID writes.
        var vJoyPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _startVJoy.Click += (_, _) => StartVJoy();
        _stopVJoy.Click += (_, _) => StopVJoy();
        _deadzone.ValueChanged += (_, _) =>
            Volatile.Write(ref _deadzonePercent, (int)_deadzone.Value);
        _invertY.CheckedChanged += (_, _) =>
            Volatile.Write(ref _invertYAxis, _invertY.Checked ? 1 : 0);
        vJoyPanel.Controls.AddRange(new Control[]
        {
            _startVJoy,
            _stopVJoy,
            new Label { AutoSize = true, Text = "Deadzone %:", Padding = new Padding(3, 5, 0, 0) },
            _deadzone,
            _invertY,
            _vJoyStatus
        });
        root.Controls.Add(vJoyPanel, 0, 1);

        _devices.Dock = DockStyle.Fill;
        _devices.View = View.Details;
        _devices.FullRowSelect = true;
        _devices.HideSelection = false;
        _devices.MultiSelect = false;
        _devices.Columns.Add("Interface", 100);
        _devices.Columns.Add("Input", 75);
        _devices.Columns.Add("Output", 75);
        _devices.Columns.Add("Feature", 75);
        _devices.Columns.Add("Version", 85);
        _devices.Columns.Add("Device path", 700);
        _devices.SelectedIndexChanged += (_, _) => UpdateButtonState();
        root.Controls.Add(_devices, 0, 2);

        var monitor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        monitor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        monitor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _joystick.Dock = DockStyle.Fill;
        monitor.Controls.Add(_joystick, 0, 0);

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6 };
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _status.Text = "Not connected.";
        _axes.Text = "Thumbstick X: --    Y: --";
        _buttons.Text = "G-keys: --";
        _auxButtons.Text = "Other buttons: --";
        _rawButtons.Text = "Other button bits: --";
        _dropped.Text = "UI queue dropped: 0 (file capture records all reports)";
        foreach (var label in new[] { _status, _axes, _buttons, _auxButtons, _rawButtons, _dropped })
        {
            label.Dock = DockStyle.Fill;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            details.Controls.Add(label, 0, details.Controls.Count);
        }
        monitor.Controls.Add(details, 1, 0);
        root.Controls.Add(monitor, 0, 3);

        var logPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        logPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logHeading = new Label
        {
            Text = "Recent input reports (timestamp + raw hex; most recent 350)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        logPanel.Controls.Add(logHeading, 0, 0);
        _recent.Dock = DockStyle.Fill;
        _recent.HorizontalScrollbar = true;
        _recent.Font = new Font(FontFamily.GenericMonospace, 9f);
        logPanel.Controls.Add(_recent, 0, 1);
        root.Controls.Add(logPanel, 0, 4);

        _refreshTimer.Tick += (_, _) => DrainReports();
        _refreshTimer.Start();
        RefreshDevices();
        UpdateButtonState();
    }

    private void RefreshDevices()
    {
        _devices.BeginUpdate();
        try
        {
            _devices.Items.Clear();
            var devices = G13DeviceDiscovery.Enumerate();
            foreach (var info in devices)
            {
                var item = new ListViewItem("G13 HID")
                {
                    Tag = info
                };
                item.SubItems.Add(info.InputReportLength.ToString());
                item.SubItems.Add(info.OutputReportLength.ToString());
                item.SubItems.Add(info.FeatureReportLength.ToString());
                item.SubItems.Add(info.Version == 0 ? "?" : info.Version.ToString("X4"));
                item.SubItems.Add(info.Path);
                _devices.Items.Add(item);
            }

            if (_devices.Items.Count > 0)
            {
                var preferred = _devices.Items.Cast<ListViewItem>()
                    .FirstOrDefault(i => ((G13DeviceInfo)i.Tag!).InputReportLength > 0);
                (preferred ?? _devices.Items[0]).Selected = true;
                if (_reader is null)
                    _status.Text = "Found " + devices.Count + " G13 HID interface(s).";
            }
            else if (_reader is null)
            {
                _status.Text = "No G13 HID interface found (USB VID 046D, PID C21C).";
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Device enumeration failed: " + ex.Message;
        }
        finally
        {
            _devices.EndUpdate();
            UpdateButtonState();
        }
    }

    private async void ConnectClicked(object? sender, EventArgs e)
    {
        if (_devices.SelectedItems.Count == 0)
            return;

        var info = (G13DeviceInfo)_devices.SelectedItems[0].Tag!;
        await StopReaderAsync();

        try
        {
            var reader = G13InputReader.Open(info);
            var cancellation = new CancellationTokenSource();
            _reader = reader;
            _readCancellation = cancellation;
            _readTask = Task.Run(() => RunReaderAsync(reader, cancellation.Token));
            _status.Text = "Reading input reports (read only). Move the thumbstick.";
        }
        catch (Exception ex)
        {
            _status.Text = "Connect failed: " + ex.Message;
        }
        UpdateButtonState();
    }

    private async Task RunReaderAsync(G13InputReader reader, CancellationToken token)
    {
        try
        {
            await reader.ReadAsync(ReceiveReport, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke((MethodInvoker)(() =>
                {
                    if (!IsDisposed)
                        _status.Text = "Read stopped: " + ex.Message;
                }));
            }
            catch (InvalidOperationException) { }
        }
    }

    private void ReceiveReport(byte[] bytes)
    {
        var sample = new Sample(DateTimeOffset.Now, bytes);
        lock (_captureGate)
        {
            if (_captureWriter is not null)
            {
                _captureWriter.Write(sample.Timestamp.ToString("O"));
                _captureWriter.Write('\t');
                _captureWriter.WriteLine(Convert.ToHexString(bytes));
            }
        }

        if (G13InputReport.TryParse(bytes, out var state) && state is not null)
        {
            try
            {
                lock (_virtualGate)
                {
                    _vJoy?.Update(
                        state.X, state.Y,
                        Volatile.Read(ref _deadzonePercent) / 100.0,
                        Volatile.Read(ref _invertYAxis) != 0);
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _vJoyErrorPending, 1) == 0 &&
                    !IsDisposed && IsHandleCreated)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)(() =>
                        {
                            if (IsDisposed) return;
                            StopVJoy();
                            _vJoyStatus.Text = "vJoy error: " + ex.Message;
                        }));
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }

        _incoming.Enqueue(sample);
        Interlocked.Increment(ref _pendingCount);
        if (Volatile.Read(ref _pendingCount) > MaxQueuedReports &&
            _incoming.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _pendingCount);
            Interlocked.Increment(ref _droppedCount);
        }
    }

    private void DrainReports()
    {
        Sample? last = null;
        _recent.BeginUpdate();
        try
        {
            for (int i = 0; i < 200 && _incoming.TryDequeue(out var sample); i++)
            {
                Interlocked.Decrement(ref _pendingCount);
                last = sample;
                _recent.Items.Add(sample.Timestamp.ToString("HH:mm:ss.fff") +
                    "  " + Convert.ToHexString(sample.Bytes));
            }

            while (_recent.Items.Count > MaxRecentReports)
                _recent.Items.RemoveAt(0);

            if (_recent.Items.Count > 0)
                _recent.TopIndex = _recent.Items.Count - 1;
        }
        finally { _recent.EndUpdate(); }

        if (last is not null)
        {
            if (G13InputReport.TryParse(last.Bytes, out var state) && state is not null)
            {
                _axes.Text = "Thumbstick X: " + state.X + " / 255    Y: " +
                    state.Y + " / 255 (raw, uncalibrated)";
                _buttons.Text = "G-keys: " +
                    (string.Join(", ", state.PressedGKeys.Select(n => "G" + n)) is var keys &&
                     keys.Length > 0 ? keys : "(none)");
                _auxButtons.Text = "Other buttons: " +
                    (string.Join(", ", state.PressedAuxiliaryKeys) is var aux &&
                     aux.Length > 0 ? aux : "(none)");
                _rawButtons.Text = "Button bytes [3..7]: " +
                    Convert.ToHexString(state.ButtonBytes.Span);
                _joystick.SetPosition(state.X, state.Y);
            }
            else
            {
                _axes.Text = "Unknown input packet: " + Convert.ToHexString(last.Bytes);
            }
        }

        _dropped.Text = "UI queue dropped: " + Volatile.Read(ref _droppedCount) +
            " (file capture records all received reports)";
    }

    private async Task StopReaderAsync()
    {
        var cancellation = _readCancellation;
        var reader = _reader;
        var readTask = _readTask;
        _readCancellation = null;
        _reader = null;
        _readTask = null;

        cancellation?.Cancel();
        reader?.Dispose();
        if (readTask is not null)
        {
            try { await readTask; }
            catch (Exception) { /* The reader reports failures to the status label. */ }
        }

        cancellation?.Dispose();
        // Always release the virtual device to prevent a stuck axis when unplugged.
        StopVJoy();
        _status.Text = "Disconnected.";
        UpdateButtonState();
    }

    private void StartCaptureClicked(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Capture all received HID input reports",
            Filter = "Tab-separated values (*.tsv)|*.tsv|All files (*.*)|*.*",
            FileName = "G13_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".tsv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            lock (_captureGate)
            {
                _captureWriter?.Dispose();
                _captureWriter = new StreamWriter(
                    dialog.FileName, append: false, Encoding.UTF8) { AutoFlush = true };
                _captureWriter.WriteLine("Timestamp\tRawHex");
            }
            _status.Text = "Capturing every received report to: " + dialog.FileName;
        }
        catch (Exception ex)
        {
            _status.Text = "Capture failed: " + ex.Message;
        }
        UpdateButtonState();
    }

    private void StopCapture()
    {
        lock (_captureGate)
        {
            _captureWriter?.Dispose();
            _captureWriter = null;
        }
        UpdateButtonState();
    }

    private void StartVJoy()
    {
        if (_reader is null) return;

        try
        {
            lock (_virtualGate)
            {
                _vJoy?.Dispose();
                _vJoy = VJoyDevice.Open(1);
            }
            Interlocked.Exchange(ref _vJoyErrorPending, 0);
            _vJoyStatus.Text = "vJoy #1 active: analog X/Y only.";
        }
        catch (Exception ex)
        {
            _vJoyStatus.Text = "vJoy unavailable: " + ex.Message;
        }
        UpdateButtonState();
    }

    private void StopVJoy()
    {
        lock (_virtualGate)
        {
            _vJoy?.Dispose();
            _vJoy = null;
        }
        _vJoyStatus.Text = "vJoy: off.";
        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        _connect.Enabled = _reader is null && _devices.SelectedItems.Count > 0;
        _disconnect.Enabled = _reader is not null;
        _startCapture.Enabled = _captureWriter is null;
        _stopCapture.Enabled = _captureWriter is not null;
        bool vJoyActive;
        lock (_virtualGate) vJoyActive = _vJoy is not null;
        _startVJoy.Enabled = _reader is not null && !vJoyActive;
        _stopVJoy.Enabled = vJoyActive;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Stop();
        _readCancellation?.Cancel();
        _reader?.Dispose();
        StopVJoy();
        StopCapture();
        base.OnFormClosed(e);
    }
}
