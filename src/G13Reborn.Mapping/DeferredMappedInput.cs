namespace G13Reborn.Mapping;

/// <summary>
/// Holds ordinary physical G-key output edges while a text macro is being
/// typed. Otherwise a mapped key press can interrupt its text midway.
/// The owner must synchronize calls with the same lock used for the feeder.
/// Stop/profile switches discard pending edges, normal completion replays
/// them in order. This class contains no OS calls and is fully testable.
/// </summary>
public sealed class DeferredMappedInput
{
    private const int MaxPendingEvents = 512;
    private readonly List<InputEvent> _pending = new();
    private bool _active;

    public bool IsDeferring => _active;

    public void Begin()
    {
        if (_active)
            throw new InvalidOperationException("Text input deferral is already active.");
        _pending.Clear();
        _active = true;
    }

    public IReadOnlyList<InputEvent> Receive(InputEvent action)
    {
        if (!_active)
            return new[] { action };

        // Cap memory use if hardware is generating events continuously.
        if (_pending.Count < MaxPendingEvents)
            _pending.Add(action);
        return Array.Empty<InputEvent>();
    }

    public IReadOnlyList<InputEvent> End(bool replay)
    {
        var result = replay ? _pending.ToArray() : Array.Empty<InputEvent>();
        _pending.Clear();
        _active = false;
        return result;
    }
}
