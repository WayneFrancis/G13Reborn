namespace G13Reborn.Mapping;

/// <summary>
/// Single-run, cancellable macro preview. Owns every key and mouse button it
/// presses and releases outstanding inputs on cancellation, failure and normal
/// completion. The input callbacks are injected so the entire lifetime can
/// be tested without calling Win32.
/// </summary>
public sealed class MacroPreviewRunner
{
    private readonly object _gate = new();
    private readonly Action<InputEvent> _send;
    private readonly Action<string> _typeText;
    private readonly HashSet<int> _heldKeys = new();
    private readonly HashSet<int> _heldMouse = new();
    private CancellationTokenSource? _cancel;
    private bool _running;

    public MacroPreviewRunner(Action<InputEvent> send, Action<string> typeText)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _typeText = typeText ?? throw new ArgumentNullException(nameof(typeText));
    }

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public async Task RunAsync(
        MacroDefinition macro, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macro);
        // Do not hold the output lock while parsing / validating.
        macro.Validate();

        CancellationTokenSource run;
        lock (_gate)
        {
            if (_running)
                throw new InvalidOperationException("A macro test is already running.");
            cancellationToken.ThrowIfCancellationRequested();
            run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancel = run;
            _running = true;
        }

        try
        {
            await MacroPlayback.ExecuteAsync(
                macro, action => Emit(action, run.Token),
                text => EmitText(text, run.Token), run.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                // Release even after a cancelled KeyPress / MouseClick,
                // where playback intentionally did not send its KeyUp.
                try { ReleaseHeld(); }
                finally
                {
                    _running = false;
                    _cancel = null;
                    run.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Stops future events and releases any currently injected key/buttons
    /// before returning. It is safe to call while the worker is in a delay.
    /// </summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (!_running) return;
            _cancel?.Cancel();
            ReleaseHeld();
        }
    }

    private void Emit(InputEvent action, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            switch (action.Kind)
            {
                case InputEventKind.KeyDown:
                    if (_heldKeys.Add(action.Code)) _send(action);
                    break;
                case InputEventKind.KeyUp:
                    if (_heldKeys.Remove(action.Code)) _send(action);
                    break;
                case InputEventKind.MouseDown:
                    if (_heldMouse.Add(action.Code)) _send(action);
                    break;
                case InputEventKind.MouseUp:
                    if (_heldMouse.Remove(action.Code)) _send(action);
                    break;
                default:
                    _send(action);
                    break;
            }
        }
    }

    private void EmitText(string text, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            _typeText(text);
        }
    }

    private void ReleaseHeld()
    {
        // Non-modifiers before modifiers, as in the normal forwarding path.
        foreach (int key in _heldKeys.OrderBy(k =>
            KeyboardChord.IsModifier(k) ? 1 : 0).ToArray())
        {
            _heldKeys.Remove(key);
            try { _send(new InputEvent(InputEventKind.KeyUp, key)); }
            catch { /* attempt all remaining releases */ }
        }
        foreach (int button in _heldMouse.Order().ToArray())
        {
            _heldMouse.Remove(button);
            try { _send(new InputEvent(InputEventKind.MouseUp, button)); }
            catch { /* attempt all remaining releases */ }
        }
    }
}
