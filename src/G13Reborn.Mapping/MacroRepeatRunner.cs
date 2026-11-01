namespace G13Reborn.Mapping;

/// <summary>Actions for a physical button transition.</summary>
public enum MacroRepeatAction { Ignore, Start, Stop }

/// <summary>
/// Stateless trigger rules. "isActive" means that this particular physical
/// G-key currently owns the single running macro, not another mapped G-key.
/// </summary>
public static class MacroRepeatPolicy
{
    public static MacroRepeatAction OnTransition(
        MacroRepeatMode mode, bool isPressed, bool isActive)
    {
        return mode switch
        {
            MacroRepeatMode.Once => isPressed ? MacroRepeatAction.Start :
                MacroRepeatAction.Ignore,
            MacroRepeatMode.WhileHeld => isPressed ? MacroRepeatAction.Start :
                (isActive ? MacroRepeatAction.Stop : MacroRepeatAction.Ignore),
            MacroRepeatMode.Toggle => !isPressed ? MacroRepeatAction.Ignore :
                (isActive ? MacroRepeatAction.Stop : MacroRepeatAction.Start),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
}

/// <summary>
/// Runs one pass or repeats until cancelled by the host's G-key release,
/// second toggle press, profile switch or forwarding stop. Releasing
/// macro-held keys between complete passes prevents accumulating holds.
/// A minimum pause prevents zero-delay macros from spinning at full speed.
/// </summary>
public static class MacroRepeatRunner
{
    public const int MinimumRepeatGapMs = 25;

    public static async Task ExecuteAsync(
        MacroDefinition macro,
        Action<InputEvent> emit,
        Action<string> emitText,
        Action releaseMacroHeld,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(releaseMacroHeld);
        macro.Validate();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await MacroPlayback.ExecuteAsync(
                macro, emit, emitText, cancellationToken).ConfigureAwait(false);
            if (macro.RepeatMode == MacroRepeatMode.Once)
                return;
            cancellationToken.ThrowIfCancellationRequested();
            releaseMacroHeld();
            await Task.Delay(MinimumRepeatGapMs, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
