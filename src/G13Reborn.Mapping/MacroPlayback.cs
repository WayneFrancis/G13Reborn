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
        Action<char> emitCharacter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(emit);
        ArgumentNullException.ThrowIfNull(emitCharacter);
        macro.Validate();

        if (macro.Mode == MacroMode.Text)
        {
            // Preserve newlines as Enter, tabs as Tab. Send ordinary UTF-16
            // code units through KEYEVENTF_UNICODE (host implementation).
            for (int i = 0; i < macro.Text.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                char ch = macro.Text[i];
                if (ch == '\r')
                {
                    if (i + 1 < macro.Text.Length && macro.Text[i + 1] == '\n')
                        i++;
                    SendTap(emit, 0x0D);
                }
                else if (ch == '\n')
                    SendTap(emit, 0x0D);
                else if (ch == '\t')
                    SendTap(emit, 0x09);
                else
                    emitCharacter(ch);
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
