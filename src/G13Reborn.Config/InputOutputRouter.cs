using G13Reborn.Mapping;

namespace G13Reborn.Config;

/// <summary>
/// Arbitrates normal mappings and macro-held keyboard/mouse controls so
/// releasing one source never releases the same physical OS key held by the
/// other. All calls must be protected by the host's single output lock.
/// </summary>
internal sealed class InputOutputRouter
{
    private readonly HashSet<int> _normalKeys = new();
    private readonly HashSet<int> _macroKeys = new();
    private readonly HashSet<int> _normalMouse = new();
    private readonly HashSet<int> _macroMouse = new();

    public void ApplyNormal(InputEvent action) => Apply(action, isMacro: false);
    public void ApplyMacro(InputEvent action) => Apply(action, isMacro: true);

    private void Apply(InputEvent action, bool isMacro)
    {
        switch (action.Kind)
        {
            case InputEventKind.KeyDown:
            case InputEventKind.KeyUp:
            {
                HashSet<int> own = isMacro ? _macroKeys : _normalKeys;
                HashSet<int> other = isMacro ? _normalKeys : _macroKeys;
                bool down = action.Kind == InputEventKind.KeyDown;
                bool changed = down ? own.Add(action.Code) : own.Remove(action.Code);
                if (changed && !other.Contains(action.Code))
                    Win32InputSender.Send(action);
                break;
            }
            case InputEventKind.MouseDown:
            case InputEventKind.MouseUp:
            {
                HashSet<int> own = isMacro ? _macroMouse : _normalMouse;
                HashSet<int> other = isMacro ? _normalMouse : _macroMouse;
                bool down = action.Kind == InputEventKind.MouseDown;
                bool changed = down ? own.Add(action.Code) : own.Remove(action.Code);
                if (changed && !other.Contains(action.Code))
                    Win32InputSender.Send(action);
                break;
            }
            case InputEventKind.Wheel:
                Win32InputSender.Send(action);
                break;
        }
    }

    public void ReleaseMacro()
    {
        foreach (int key in _macroKeys.OrderBy(k =>
            KeyboardChord.IsModifier(k) ? 1 : 0).ThenBy(k => k).ToArray())
            ApplyMacro(new InputEvent(InputEventKind.KeyUp, key));
        foreach (int button in _macroMouse.Order().ToArray())
            ApplyMacro(new InputEvent(InputEventKind.MouseUp, button));
    }

    public void ReleaseNormal()
    {
        foreach (int key in _normalKeys.OrderBy(k =>
            KeyboardChord.IsModifier(k) ? 1 : 0).ThenBy(k => k).ToArray())
            ApplyNormal(new InputEvent(InputEventKind.KeyUp, key));
        foreach (int button in _normalMouse.Order().ToArray())
            ApplyNormal(new InputEvent(InputEventKind.MouseUp, button));
    }

    public void ReleaseAll()
    {
        ReleaseMacro();
        ReleaseNormal();
    }
}
