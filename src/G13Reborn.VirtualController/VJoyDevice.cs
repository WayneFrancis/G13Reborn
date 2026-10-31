using System.Runtime.InteropServices;

namespace G13Reborn.VirtualController;

/// <summary>
/// Optional vJoy DirectInput backend. Requires the installed vJoy interface DLL
/// and its Windows driver. Never installs or modifies either one.
/// Synchronize Update/Dispose calls externally when used from multiple threads.
///
/// The native vJoyInterface.dll creates a hidden window and registers a Win32
/// window class during InitDll(). Its code does not fully tear that state down
/// when unloaded, so unloading and reloading it in the same process breaks
/// subsequent acquisitions. Keep one process-wide DLL reference until exit,
/// while releasing each acquired vJoy device normally on Dispose().
/// </summary>
public sealed class VJoyDevice : IDisposable
{
    private const uint HidUsageX = 0x30;
    private const uint HidUsageY = 0x31;

    // This handle is intentionally process-lifetime. vJoyInterface creates a
    // hidden notification window on first initialization, and calling
    // FreeLibrary between Start/Stop cycles can leave its registered window
    // class behind. The OS unloads the module at process exit.
    private static readonly object LibraryGate = new();
    private static IntPtr s_library;

    private readonly uint _id;
    private IntPtr _dll;
    private bool _owned;
    private readonly BoolNoArgs _enabled;
    private readonly Status _status;
    private readonly Acquire _acquire;
    private readonly Release _release;
    private readonly AxisExists _axisExists;
    private readonly AxisRange _axisMin;
    private readonly AxisRange _axisMax;
    private readonly SetAxis _setAxis;
    private readonly GetButtonCount _getButtonCount;
    private readonly SetButton _setButton;
    private uint _currentButtons;
    private readonly Reset _reset;
    private readonly (int Min, int Max) _xRange;
    private readonly (int Min, int Max) _yRange;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool BoolNoArgs();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Status(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool Acquire(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Release(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool AxisExists(uint id, uint usage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool AxisRange(uint id, uint usage, out int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool SetAxis(int value, uint id, uint usage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool Reset(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetButtonCount(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool SetButton(
        [MarshalAs(UnmanagedType.Bool)] bool pressed, uint deviceId, byte button);

    private VJoyDevice(IntPtr dll, uint id)
    {
        _dll = dll;
        _id = id;
        _enabled = Get<BoolNoArgs>("vJoyEnabled");
        _status = Get<Status>("GetVJDStatus");
        _acquire = Get<Acquire>("AcquireVJD");
        _release = Get<Release>("RelinquishVJD");
        _axisExists = Get<AxisExists>("GetVJDAxisExist");
        _axisMin = Get<AxisRange>("GetVJDAxisMin");
        _axisMax = Get<AxisRange>("GetVJDAxisMax");
        _setAxis = Get<SetAxis>("SetAxis");
        _reset = Get<Reset>("ResetVJD");
        _getButtonCount = Get<GetButtonCount>("GetVJDButtonNumber");
        _setButton = Get<SetButton>("SetBtn");

        if (!_enabled())
            throw new InvalidOperationException("vJoy is installed but its driver is disabled.");

        int status = _status(id);
        if (status != 1) // VJD_STAT_FREE
            throw new InvalidOperationException(status switch
            {
                0 => $"vJoy device {id} is already owned by this process.",
                2 => $"vJoy device {id} is busy (another feeder owns it).",
                3 => $"vJoy device {id} is missing. Enable it in vJoyConf.",
                _ => $"vJoy device {id} is unavailable (status {status})."
            });

        if (!_axisExists(id, HidUsageX) || !_axisExists(id, HidUsageY))
            throw new InvalidOperationException(
                $"vJoy device {id} must have both X and Y axes enabled in vJoyConf.");

        _xRange = GetRange(HidUsageX);
        _yRange = GetRange(HidUsageY);
        ButtonCount = Math.Clamp(_getButtonCount(id), 0, 128);

        if (!_acquire(id))
            throw new InvalidOperationException($"Unable to acquire vJoy device {id}.");
        _owned = true;
        if (!_reset(id))
        {
            // The device has been acquired: release it before constructor failure.
            _release(id);
            _owned = false;
            throw new InvalidOperationException($"Unable to reset vJoy device {id}.");
        }
    }

    /// <summary>Number of virtual buttons configured for this vJoy device.</summary>
    public int ButtonCount { get; }

    public static VJoyDevice Open(uint deviceId = 1)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("G13Reborn vJoy output requires 64-bit Windows.");

        if (deviceId is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(deviceId));

        return new VJoyDevice(GetProcessLibrary(), deviceId);
    }

    /// <summary>
    /// Load the native interface once per process, not once per forwarding
    /// session. Failed loads are not cached, so a later retry remains possible.
    /// No FreeLibrary is issued: vJoy owns a process-wide hidden notification
    /// window with no reliable unload/reinitialize mechanism.
    /// </summary>
    private static IntPtr GetProcessLibrary()
    {
        lock (LibraryGate)
        {
            if (s_library == IntPtr.Zero)
                s_library = LoadInterface();
            return s_library;
        }
    }

    private T Get<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_dll, name));

    private (int Min, int Max) GetRange(uint usage)
    {
        if (!_axisMin(_id, usage, out int min) ||
            !_axisMax(_id, usage, out int max) || min >= max)
            throw new InvalidOperationException(
                $"Unable to read vJoy axis range for usage 0x{usage:X}.");
        return (min, max);
    }

    private static int ConvertAxis(double normalized, (int Min, int Max) range)
    {
        normalized = Math.Clamp(normalized, -1, 1);
        return (int)Math.Round(
            range.Min + (normalized + 1) / 2 * (range.Max - (double)range.Min),
            MidpointRounding.AwayFromZero);
    }

    /// <summary>Move analog axes. Called on each input report, not on the WinForms UI timer.</summary>
    public void Update(byte rawX, byte rawY, double deadzone, bool invertY)
    {
        if (!_owned || _dll == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(VJoyDevice));

        var (x, y) = ThumbstickMapper.Map(rawX, rawY, deadzone, invertY);
        if (!_setAxis(ConvertAxis(x, _xRange), _id, HidUsageX) ||
            !_setAxis(ConvertAxis(y, _yRange), _id, HidUsageY))
            throw new InvalidOperationException("vJoy failed to update X/Y axes. Check device ownership.");
    }

    /// <summary>
    /// Forward only changed button states, so multiple G13 keys mapped to one
    /// virtual button work together. Supports vJoy buttons 1 through 32.
    /// Caller must hold the same synchronization lock used for analog updates.
    /// </summary>
    public void UpdateButtons(uint desiredMask)
    {
        if (!_owned || _dll == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(VJoyDevice));

        int count = Math.Min(ButtonCount, 32);
        uint validMask = count == 32 ? uint.MaxValue :
            count == 0 ? 0 : (1u << count) - 1u;
        desiredMask &= validMask;

        uint changed = _currentButtons ^ desiredMask;
        for (int bit = 0; bit < count; bit++)
        {
            uint mask = 1u << bit;
            if ((changed & mask) == 0)
                continue;

            bool pressed = (desiredMask & mask) != 0;
            if (!_setButton(pressed, _id, (byte)(bit + 1)))
                throw new InvalidOperationException(
                    $"Could not set virtual button {bit + 1} to {(pressed ? "pressed" : "released")}.");

            // Update per button so failures leave the correct partial state.
            if (pressed) _currentButtons |= mask;
            else _currentButtons &= ~mask;
        }
    }

    public void ResetToNeutral()
    {
        if (_owned && _dll != IntPtr.Zero)
        {
            _reset(_id);
            _currentButtons = 0;
        }
    }

    public void Dispose()
    {
        if (_dll == IntPtr.Zero)
            return;
        try
        {
            if (_owned)
            {
                _reset(_id);
                _currentButtons = 0;
                _release(_id);
                _owned = false;
            }
        }
        finally
        {
            // Mark this managed device session as disposed. The shared native
            // library stays loaded until the entire G13Reborn process exits.
            _dll = IntPtr.Zero;
        }
    }

    private static IntPtr LoadInterface()
    {
        var candidates = new List<string>();
        string? configured = Environment.GetEnvironmentVariable("VJOY_INTERFACE_DLL");
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured);

        foreach (var baseDir in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(baseDir, "vJoy", "x64", "vJoyInterface.dll"));
            candidates.Add(Path.Combine(baseDir, "vJoy", "vJoyInterface.dll"));
        }

        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out IntPtr loaded))
                return loaded;
        }

        // Also support a DLL on PATH if installed to a custom directory.
        if (NativeLibrary.TryLoad("vJoyInterface.dll", out IntPtr fallback))
            return fallback;

        throw new DllNotFoundException(
            "vJoyInterface.dll (64-bit) was not found. Install a supported vJoy " +
            "release or set VJOY_INTERFACE_DLL to its full path, then restart G13Reborn.");
    }
}
