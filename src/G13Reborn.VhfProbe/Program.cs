using System.ComponentModel;
using System.Runtime.InteropServices;
using G13Reborn.Mapping;
using Microsoft.Win32.SafeHandles;

namespace G13Reborn.VhfProbe;

/// <summary>
/// Opt-in test client, separate from normal G13Reborn forwarding.
/// Requires a manually built, signed and installed experimental driver.
/// An elevated terminal is expected (the kernel control path is admin-only).
/// </summary>
internal static class Program
{
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle file, uint controlCode,
        [In] byte[] inputBuffer, int inputLength,
        nint outputBuffer, int outputLength,
        out int bytesReturned, nint overlapped);

    private static int Main(string[] args)
    {
        if (args.Length != 1 ||
            args[0] is not ("--probe" or "--test-k"))
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project src/G13Reborn.VhfProbe -- --probe");
            Console.WriteLine("  dotnet run --project src/G13Reborn.VhfProbe -- --test-k");
            Console.WriteLine();
            Console.WriteLine("Source-only native prototype; not installed by this tool.");
            Console.WriteLine("Run only in an isolated, administrator-controlled test VM.");
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("VHF is a Windows-only kernel driver framework.");
            return 2;
        }

        using var handle = CreateFileW(
            VirtualHidKeyboardProtocol.DevicePath, GenericWrite,
            0, nint.Zero, OpenExisting, 0, nint.Zero);
        if (handle.IsInvalid)
        {
            var failure = new Win32Exception(Marshal.GetLastWin32Error());
            Console.Error.WriteLine(
                $"Could not open {VirtualHidKeyboardProtocol.DevicePath}: " +
                failure.Message);
            Console.Error.WriteLine(
                "Expected until a reviewed, signed driver is installed on " +
                "an isolated Windows test machine. Administrator rights required.");
            return 1;
        }

        Console.WriteLine("Opened experimental VHF control device.");
        if (args[0] == "--probe")
        {
            Console.WriteLine("Probe succeeded. NO keyboard reports were sent.");
            return 0;
        }

        Console.WriteLine(
            "Sending HID keyboard usage 0x0E (K) down and up.");
        try
        {
            Send(handle, VirtualHidKeyboardProtocol.ReleaseAll());
            Send(handle, VirtualHidKeyboardProtocol.Encode(
                VirtualHidKeyboardProtocol.CreateReport(
                    0, VirtualHidKeyboardProtocol.HidUsageK)));
            Thread.Sleep(150);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("VHF probe failed: " + ex.Message);
            return 1;
        }
        finally
        {
            // Also handled in the kernel file-object cleanup callback.
            try { Send(handle, VirtualHidKeyboardProtocol.ReleaseAll()); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    "Release report failed: " + ex.Message +
                    " (closing the driver handle also releases keys).");
            }
        }
    }

    private static void Send(SafeFileHandle handle, byte[] packet)
    {
        if (!DeviceIoControl(
                handle, VirtualHidKeyboardProtocol.IoctlSendKeyboard,
                packet, packet.Length, nint.Zero, 0,
                out _, nint.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Driver rejected the keyboard report.");
    }
}
