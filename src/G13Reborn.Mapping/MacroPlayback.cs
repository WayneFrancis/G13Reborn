namespace G13Reborn.Mapping;

/// <summary>
/// Executes the sequence without owning the Windows output state. The host
/// owns key/mouse lifetime and must release all macro-held controls on
/// cancellation, profile changes and shutdown.
/// </summary>
public static class MacroPlayback
{
    private const int TapDurationMs = 35;
    private const int DoubleClickIntervalMs = 65;

    public static async Task ExecuteAsync(
        MacroDefinition macro,
        Action<InputEvent> emit,
        Action<string> emitText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(emit);
        ArgumentNullException.ThrowIfNull(emitText);
        macro.Validate();

        if (macro.Mode == MacroMode.Text)
        {
            // A SendInput call guarantees the entire event array is inserted
            // without other keyboard events interspersed. Sending each
            // character individually let live G-key mappings interrupt text.
            //
            // Split at Enter/Tab and every 128 UTF-16 code units to preserve
            // cancellation for long macros. A short greeting is one atomic
            // native call. Delay mode intentionally submits one character
            // at a time for applications that require pacing.
            const int maxChunk = 128;
            int index = 0;
            while (index < macro.Text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                char next = macro.Text[index];
                if (next is '\r' or '\n' or '\t')
                {
                    if (next == '\r' && index + 1 < macro.Text.Length &&
                        macro.Text[index + 1] == '\n')
                        index++;
                    SendTap(emit, next == '\t' ? 0x09 : 0x0D);
                    index++;
                }
                else
                {
                    int count = 0;
                    int max = macro.TextDelayMs > 0 ? 1 : maxChunk;
                    while (index + count < macro.Text.Length &&
                           count < max &&
                           macro.Text[index + count] is not ('\r' or '\n' or '\t'))
                        count++;

                    // Avoid splitting Unicode surrogate pairs across chunks.
                    if (count > 0 && index + count < macro.Text.Length &&
                        char.IsHighSurrogate(macro.Text[index + count - 1]) &&
                        char.IsLowSurrogate(macro.Text[index + count]))
                        count++;

                    emitText(macro.Text.Substring(index, count));
                    index += count;
                }

                if (macro.TextDelayMs > 0 && index < macro.Text.Length)
                    await Task.Delay(macro.TextDelayMs, cancellationToken)
                        .ConfigureAwait(false);
            }
            return;
        }

        foreach (MacroStep step in macro.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (step.Kind)
            {
                case MacroStepKind.Delay:
                    break;
                case MacroStepKind.KeyDown:
                    emit(new InputEvent(InputEventKind.KeyDown, MacroStep.ParseKey(step.Parameter)));
                    break;
                case MacroStepKind.KeyUp:
                    emit(new InputEvent(InputEventKind.KeyUp, MacroStep.ParseKey(step.Parameter)));
                    break;
                case MacroStepKind.KeyPress:
                {
                    int key = MacroStep.ParseKey(step.Parameter);
                    emit(new InputEvent(InputEventKind.KeyDown, key));
                    await Task.Delay(TapDurationMs, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    emit(new InputEvent(InputEventKind.KeyUp, key));
                    break;
                }
                case MacroStepKind.MouseDown:
                    emit(new InputEvent(InputEventKind.MouseDown, MacroStep.ParseMouse(step.Parameter)));
                    break;
                case MacroStepKind.MouseUp:
                    emit(new InputEvent(InputEventKind.MouseUp, MacroStep.ParseMouse(step.Parameter)));
                    break;
                case MacroStepKind.MouseClick:
                    await ClickAsync(step.Parameter, emit, cancellationToken).ConfigureAwait(false);
                    break;
                case MacroStepKind.MouseDoubleClick:
                    await ClickAsync(step.Parameter, emit, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(DoubleClickIntervalMs, cancellationToken).ConfigureAwait(false);
                    await ClickAsync(step.Parameter, emit, cancellationToken).ConfigureAwait(false);
                    break;
                case MacroStepKind.WheelUp:
                    emit(new InputEvent(InputEventKind.Wheel, 120));
                    break;
                case MacroStepKind.WheelDown:
                    emit(new InputEvent(InputEventKind.Wheel, -120));
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported macro step {step.Kind}.");
            }

            if (step.DelayAfterMs > 0)
                await Task.Delay(step.DelayAfterMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ClickAsync(
        string mouseButton, Action<InputEvent> emit, CancellationToken cancellationToken)
    {
        int button = MacroStep.ParseMouse(mouseButton);
        emit(new InputEvent(InputEventKind.MouseDown, button));
        await Task.Delay(TapDurationMs, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        emit(new InputEvent(InputEventKind.MouseUp, button));
    }

    private static void SendTap(Action<InputEvent> emit, int key)
    {
        emit(new InputEvent(InputEventKind.KeyDown, key));
        emit(new InputEvent(InputEventKind.KeyUp, key));
    }
}
