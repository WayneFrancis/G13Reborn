using System.Windows.Forms;

namespace G13Reborn.Mapping;

/// <summary>
/// Converts real keyboard/mouse press/release edges into a step sequence.
/// No hooks or OS injection: the WinForms recorder supplies observed events
/// and monotonic elapsed milliseconds. A recording is only saved after
/// explicit user acceptance in the editor.
/// </summary>
public sealed class MacroRecordingSession
{
    public const int MaximumSteps = 200;
    public const long MaximumDurationMs = 60_000;

    private readonly List<MacroStep> _steps = new();
    private readonly HashSet<int> _heldKeys = new();
    private readonly HashSet<int> _heldMouse = new();
    private long _lastEventAt;
    private bool _finished;

    public int StepCount => _steps.Count;
    public bool IsFinished => _finished;
    public bool AtLimit => _steps.Count >= MaximumSteps - 12;
    public IReadOnlyList<MacroStep> Steps => _steps.Select(s => s.Clone()).ToList();

    public bool RecordKeyboard(uint virtualKey, bool pressed, long elapsedMs)
    {
        if (_finished || elapsedMs is < 0 or > MaximumDurationMs || AtLimit)
            return false;

        // Ignore Windows' repeated KeyDown events for a physically held key.
        int key = NormalizeModifiers((int)virtualKey);
        string? name = GetKeyName(key);
        if (name is null) return false;
        if (pressed ? !_heldKeys.Add(key) : !_heldKeys.Remove(key))
            return false;

        Append(pressed ? MacroStepKind.KeyDown : MacroStepKind.KeyUp,
            name, elapsedMs);
        return true;
    }

    public bool RecordMouseButton(string mouseButton, bool pressed, long elapsedMs)
    {
        if (_finished || elapsedMs is < 0 or > MaximumDurationMs || AtLimit)
            return false;
        int button = MacroStep.ParseMouse(mouseButton);
        if (pressed ? !_heldMouse.Add(button) : !_heldMouse.Remove(button))
            return false;
        Append(pressed ? MacroStepKind.MouseDown : MacroStepKind.MouseUp,
            mouseButton, elapsedMs);
        return true;
    }

    public bool RecordWheel(int delta, long elapsedMs)
    {
        if (_finished || elapsedMs is < 0 or > MaximumDurationMs || AtLimit ||
            delta == 0)
            return false;

        // Standard Windows wheel messages use multiples of WHEEL_DELTA=120.
        // For high-resolution wheels, round to at least one step.
        int steps = Math.Clamp((Math.Abs(delta) + 60) / 120, 1, 12);
        var kind = delta > 0 ? MacroStepKind.WheelUp : MacroStepKind.WheelDown;
        bool recorded = false;
        while (steps-- > 0 && !AtLimit)
        {
            Append(kind, "", elapsedMs);
            recorded = true;
        }
        return recorded;
    }

    /// <summary>
    /// Finish with balanced key/button releases, even if recording stopped
    /// while a physical key was being held.
    /// </summary>
    public IReadOnlyList<MacroStep> Finish(long elapsedMs)
    {
        if (_finished) return Steps;
        elapsedMs = Math.Clamp(elapsedMs, 0, MaximumDurationMs);
        foreach (int key in _heldKeys.OrderBy(k =>
                     KeyboardChord.IsModifier(k) ? 1 : 0).ToArray())
        {
            Append(MacroStepKind.KeyUp, GetKeyName(key)!, elapsedMs);
        }
        foreach (int button in _heldMouse.Order().ToArray())
        {
            string name = button switch
            {
                (int)MouseControl.Left => "Left",
                (int)MouseControl.Right => "Right",
                _ => "Middle"
            };
            Append(MacroStepKind.MouseUp, name, elapsedMs);
        }
        _heldKeys.Clear();
        _heldMouse.Clear();
        _finished = true;
        return Steps;
    }

    private void Append(MacroStepKind kind, string parameter, long elapsedMs)
    {
        if (_steps.Count > 0)
        {
            long difference = Math.Max(0, elapsedMs - _lastEventAt);
            _steps[^1].DelayAfterMs = (int)Math.Min(difference, MaximumDurationMs);
        }
        _steps.Add(new MacroStep
        {
            Kind = kind, Parameter = parameter, DelayAfterMs = 0
        });
        _lastEventAt = elapsedMs;
    }

    private static int NormalizeModifiers(int key) => key switch
    {
        0xA0 or 0xA1 => 0x10, // left/right Shift
        0xA2 or 0xA3 => 0x11, // left/right Ctrl
        0xA4 or 0xA5 => 0x12, // left/right Alt
        _ => key
    };

    private static string? GetKeyName(int virtualKey)
    {
        if (virtualKey is < 1 or > 255)
            return null;

        string name = virtualKey switch
        {
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x5B => "Win",
            0x5C => "RWin",
            _ => ((Keys)virtualKey).ToString()
        };
        try
        {
            // Do not record a key that the playback parser cannot reproduce.
            return MacroStep.ParseKey(name) == virtualKey ? name : null;
        }
        catch (ArgumentException) { return null; }
    }
}
