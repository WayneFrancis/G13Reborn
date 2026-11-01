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
        CancellationToken cancellationToken = default,
        Action<int, MacroStep>? onStep = null)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(emit);
        ArgumentNullException.ThrowIfNull(emitText);
        macro.Validate();

        if (macro.Mode == MacroMode.Text)
        {
            // Win32 SendInput reports that events reached the system queue,
            // not that the focused editor processed them. Injecting dozens of
            // key down/up pairs in one call can overwhelm some editors:
            // Notepad in particular can lose many characters.
            //
            // Send one Unicode scalar at a time, pace by default at 25ms,
            // and retain cancellation between each character. A serialized
            // character includes both surrogate code units when needed.
            int delay = macro.TextDelayMs == 0 ? 25 : macro.TextDelayMs;
            int index = 0;
            while (index < macro.Text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                char first = macro.Text[index];
                string? printable = null;
                int key = 0;
                if (first == '\r')
                {
                    if (index + 1 < macro.Text.Length &&
                        macro.Text[index + 1] == '\n')
                        index++;
                    key = 0x0D;
                }
                else if (first == '\n')
                    key = 0x0D;
                else if (first == '\t')
                    key = 0x09;
                else if (char.IsHighSurrogate(first) &&
                         index + 1 < macro.Text.Length &&
                         char.IsLowSurrogate(macro.Text[index + 1]))
                {
                    printable = macro.Text.Substring(index, 2);
                    index++;
                }
                else
                    printable = first.ToString();

                if (printable is not null)
                    emitText(printable);
                else
                    SendTap(emit, key);

                index++;
                if (index < macro.Text.Length)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        for (int stepIndex = 0; stepIndex < macro.Steps.Count; stepIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MacroStep step = macro.Steps[stepIndex];
            onStep?.Invoke(stepIndex, step);
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
