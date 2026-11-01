namespace G13Reborn.Mapping;

/// <summary>
/// Win32 XBUTTON1/2 codes, independent of WinForms and HID access.
/// Windows places the X button identifier in the high word of
/// MSLLHOOKSTRUCT.mouseData and in SendInput MOUSEINPUT.mouseData.
/// </summary>
public static class MouseExtraButtons
{
    public static string? DecodeHookButton(uint mouseData) =>
        (mouseData >> 16) switch
        {
            1 => "XButton1",
            2 => "XButton2",
            _ => null
        };

    public static uint MouseData(MouseControl button) => button switch
    {
        MouseControl.XButton1 => 1,
        MouseControl.XButton2 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };
}
