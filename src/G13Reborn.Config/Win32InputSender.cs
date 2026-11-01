using System.ComponentModel;
using System.Runtime.InteropServices;
using G13Reborn.Mapping;

namespace G13Reborn.Config;

/// <summary>
/// Sends genuine key/mouse press and release edges to the Windows input stream.
/// Requires a 64-bit process; does not bypass UIPI or remap hardware scan codes.
/// </summary>
internal static class Win32InputSender
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint UnicodeKey = 0x0004;
    private const uint ExtendedKey = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseXDown = 0x0080;
    private const uint MouseXUp = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    // On x64 INPUT has 8-byte alignment: type at 0, union at 8, total 40.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyInput Keyboard;
        [FieldOffset(8)] public MouseInput Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint number, [In] Input[] inputs, int structureSize);

    public static void Send(InputEvent action)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Input injection requires 64-bit Windows.");

        var input = action.Kind switch
        {
            InputEventKind.KeyDown => Key((ushort)action.Code, released: false),
            InputEventKind.KeyUp => Key((ushort)action.Code, released: true),
            InputEventKind.MouseDown => MouseButton(action.Code, released: false),
            InputEventKind.MouseUp => MouseButton(action.Code, released: true),
            InputEventKind.Wheel => new Input
            {
                Type = InputMouse,
                Mouse = new MouseInput { Flags = MouseWheel, MouseData = unchecked((uint)action.Code) }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        var inputs = new[] { input };
        if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Windows did not accept the simulated input (possibly UIPI/elevation restrictions).");
    }

    /// <summary>
    /// Send a complete UTF-16 text chunk as ONE native SendInput call.
    /// The Windows input queue preserves the ordering of the entire array
    /// without interspersing other hardware or software keyboard events.
    /// MacroPlayback limits chunk length, avoiding huge Win32 buffers.
    /// </summary>
    public static void SendUnicodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return;
        if (text.Length > 128 + 1)
            throw new ArgumentOutOfRangeException(nameof(text),
                "Text playback chunks are limited to 129 UTF-16 code units.");
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Unicode injection requires 64-bit Windows.");

        var events = new Input[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            ushort unit = text[i];
            events[i * 2] = new Input
            {
                Type = InputKeyboard,
                Keyboard = new KeyInput { Scan = unit, Flags = UnicodeKey }
            };
            events[i * 2 + 1] = new Input
            {
                Type = InputKeyboard,
                Keyboard = new KeyInput { Scan = unit, Flags = UnicodeKey | KeyUp }
            };
        }

        uint inserted = SendInput((uint)events.Length, events, Marshal.SizeOf<Input>());
        if (inserted != events.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Windows accepted only {inserted} of {events.Length} text input events.");
    }

    private static Input Key(ushort vk, bool released) => new()
    {
        Type = InputKeyboard,
        Keyboard = new KeyInput
        {
            Vk = vk,
            Flags = (released ? KeyUp : 0u) |
                (IsExtended(vk) ? ExtendedKey : 0u)
        }
    };

    private static bool IsExtended(ushort vk) =>
        vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x6F or 0x5B or 0x5C;

    private static Input MouseButton(int button, bool released)
    {
        uint flags = ((MouseControl)button, released) switch
        {
            (MouseControl.Left, false) => MouseLeftDown,
            (MouseControl.Left, true) => MouseLeftUp,
            (MouseControl.Right, false) => MouseRightDown,
            (MouseControl.Right, true) => MouseRightUp,
            (MouseControl.Middle, false) => MouseMiddleDown,
            (MouseControl.Middle, true) => MouseMiddleUp,
            (MouseControl.XButton1, false) or (MouseControl.XButton2, false) => MouseXDown,
            (MouseControl.XButton1, true) or (MouseControl.XButton2, true) => MouseXUp,
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };
        uint mouseData = button is (int)MouseControl.XButton1 or (int)MouseControl.XButton2
            ? MouseExtraButtons.MouseData((MouseControl)button) : 0;
        return new Input { Type = InputMouse, Mouse = new MouseInput
        {
            Flags = flags, MouseData = mouseData
        } };
    }
}
