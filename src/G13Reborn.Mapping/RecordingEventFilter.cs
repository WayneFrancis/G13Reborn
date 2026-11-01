namespace G13Reborn.Mapping;

/// <summary>
/// Capturing a global mouse event requires both an external foreground app
/// and a hit-test that confirms the click is not inside the recorder itself.
/// The foreground check alone is insufficient: the low-level hook runs
/// before Windows changes focus when clicking "Stop and use recording".
/// </summary>
public static class RecordingEventFilter
{
    public static bool ShouldCaptureMouse(
        bool recordingActive, bool externalForeground,
        bool mouseTargetsRecorder, bool injected) =>
        recordingActive && externalForeground && !mouseTargetsRecorder && !injected;
}
