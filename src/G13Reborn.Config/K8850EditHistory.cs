namespace G13Reborn.Config;

internal sealed class K8850EditHistory
{
    private const int Limit = 100;
    private sealed record Snapshot(Dictionary<string, string> Bindings, Dictionary<string, string> Labels,
        Dictionary<int, string> LayerNames)
    {
        public static Snapshot Capture(K8850Profile profile) =>
            new(new(profile.Bindings), new(profile.Labels), new(profile.LayerNames));

        private static bool Equal<TKey>(Dictionary<TKey, string> left, Dictionary<TKey, string> right) where TKey : notnull =>
            left.Count == right.Count && left.All(x => right.TryGetValue(x.Key, out var value) && value == x.Value);

        public bool SameAs(Snapshot other) => Equal(Bindings, other.Bindings) &&
            Equal(Labels, other.Labels) && Equal(LayerNames, other.LayerNames);

        public void Restore(K8850Profile profile)
        {
            profile.Bindings = new(Bindings);
            profile.Labels = new(Labels);
            profile.LayerNames = new(LayerNames);
        }
    }

    private sealed record Entry(string Description, Snapshot Before, Snapshot After);
    private readonly List<Entry> _undo = new();
    private readonly List<Entry> _redo = new();
    private string? _group;
    public string? UndoDescription => _undo.Count == 0 ? null : _undo[^1].Description;
    public string? RedoDescription => _redo.Count == 0 ? null : _redo[^1].Description;

    public void BreakGroup() => _group = null;

    public void Edit(K8850Profile profile, string description, Action change, string? group = null)
    {
        var before = Snapshot.Capture(profile);
        try { change(); }
        catch { before.Restore(profile); BreakGroup(); throw; }
        var after = Snapshot.Capture(profile);
        if (before.SameAs(after))
        {
            if (group == null) BreakGroup();
            return;
        }
        if (group != null && group == _group && _undo.Count > 0 && _redo.Count == 0 &&
            _undo[^1].After.SameAs(before))
        {
            var entry = _undo[^1];
            if (entry.Before.SameAs(after)) _undo.RemoveAt(_undo.Count - 1);
            else _undo[^1] = entry with { After = after };
        }
        else
        {
            _undo.Add(new(description, before, after));
            if (_undo.Count > Limit) _undo.RemoveAt(0);
        }
        _redo.Clear();
        _group = group;
    }

    public string? Undo(K8850Profile profile)
    {
        BreakGroup();
        if (_undo.Count == 0) return null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        entry.Before.Restore(profile);
        _redo.Add(entry);
        return entry.Description;
    }

    public string? Redo(K8850Profile profile)
    {
        BreakGroup();
        if (_redo.Count == 0) return null;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        entry.After.Restore(profile);
        _undo.Add(entry);
        return entry.Description;
    }
}
