using System.Diagnostics;
using System.Runtime.InteropServices;

namespace G13Reborn.Config;

/// <summary>
/// Queries the process that owns the active top-level window.
/// Unknown or protected processes return null, allowing fallback selection.
/// </summary>
internal static class ForegroundExecutable
{
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        nint hWnd, out uint processId);

    public static string? TryGetName()
    {
        nint window = GetForegroundWindow();
        if (window == 0) return null;

        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0 || processId > int.MaxValue) return null;

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return G13Reborn.Mapping.ExecutableProfileMatcher.NormalizeExecutableName(
                process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            return null;
        }
    }
}
