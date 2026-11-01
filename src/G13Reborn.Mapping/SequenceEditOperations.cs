namespace G13Reborn.Mapping;

/// <summary>
/// Pure sequence-editing operations shared by the WinForms editor and tests.
/// All operations return or modify validated, independent MacroStep instances.
/// </summary>
public static class SequenceEditOperations
{
    /// <summary>
    /// Insert a recording immediately after the selected step, or append if
    /// selectedIndex is null. Neither existing nor recorded steps are mutated.
    /// </summary>
    public static List<MacroStep> InsertRecording(
        IReadOnlyList<MacroStep> existing,
        IReadOnlyList<MacroStep> recorded,
        int? selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(recorded);
        if (recorded.Count == 0)
            throw new ArgumentException("The recording contains no steps.", nameof(recorded));
        if (selectedIndex is < 0 || selectedIndex >= existing.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        if (existing.Count + recorded.Count > MacroRecordingSession.MaximumSteps)
            throw new ArgumentException(
                $"A macro can contain at most {MacroRecordingSession.MaximumSteps} steps. " +
                $"Existing: {existing.Count}, recorded: {recorded.Count}.");

        // Validate before modifying the editor's working copy.
        foreach (var step in existing.Concat(recorded))
            step.Validate();

        var result = existing.Select(s => s.Clone()).ToList();
        int insertAt = selectedIndex.HasValue ? selectedIndex.Value + 1 : result.Count;
        result.InsertRange(insertAt, recorded.Select(s => s.Clone()));
        return result;
    }

    /// <summary>
    /// Insert a recording at the requested location. When the recording is
    /// inserted into the middle of an existing macro, its final event needs a
    /// nonzero delay before playback continues into the original suffix.
    /// Appending keeps the recording's original trailing delay unchanged.
    /// </summary>
    public static List<MacroStep> InsertRecording(
        IReadOnlyList<MacroStep> existing,
        IReadOnlyList<MacroStep> recorded,
        int? selectedIndex,
        int joinDelayMs)
    {
        if (joinDelayMs is < 0 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(joinDelayMs));

        // Preserve the original selection/size/validation checks.
        var result = InsertRecording(existing, recorded, selectedIndex);
        int insertionIndex = selectedIndex.HasValue
            ? selectedIndex.Value + 1 : existing.Count;

        if (insertionIndex < existing.Count && joinDelayMs > 0 &&
            result[insertionIndex + recorded.Count - 1].DelayAfterMs == 0)
        {
            result[insertionIndex + recorded.Count - 1].DelayAfterMs = joinDelayMs;
        }
        return result;
    }

    /// <summary>
    /// Deep-copy selected actions in original sequence order. Indexes may
    /// arrive unsorted from DataGridView.SelectedRows.
    /// </summary>
    public static List<MacroStep> CopySelected(
        IReadOnlyList<MacroStep> existing,
        IEnumerable<int> selectedIndices)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var indices = NormalizeSelectedIndices(existing.Count, selectedIndices);
        return indices.Select(i => existing[i].Clone()).ToList();
    }

    /// <summary>Remove any selection of rows, including disjoint selections.</summary>
    public static List<MacroStep> RemoveSelected(
        IReadOnlyList<MacroStep> existing,
        IEnumerable<int> selectedIndices)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var indices = NormalizeSelectedIndices(existing.Count, selectedIndices);
        var remove = indices.ToHashSet();
        return existing.Where((_, i) => !remove.Contains(i))
            .Select(s => s.Clone()).ToList();
    }

    /// <summary>
    /// Insert copied steps immediately after the final selected row. When
    /// selection is empty, append at the end. Paste is independent of source.
    /// </summary>
    public static List<MacroStep> PasteSteps(
        IReadOnlyList<MacroStep> existing,
        IReadOnlyList<MacroStep> copied,
        IEnumerable<int> selectedIndices)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(copied);
        var indices = NormalizeSelectedIndices(existing.Count, selectedIndices);
        int? anchor = indices.Length == 0 ? null : indices[^1];
        return InsertRecording(existing, copied, anchor);
    }

    public static int[] NormalizeSelectedIndices(
        int stepCount, IEnumerable<int> selectedIndices)
    {
        ArgumentNullException.ThrowIfNull(selectedIndices);
        int[] indices = selectedIndices.Distinct().Order().ToArray();
        if (indices.Any(index => index < 0 || index >= stepCount))
            throw new ArgumentOutOfRangeException(nameof(selectedIndices),
                "The selection contains a row outside the macro.");
        return indices;
    }

    /// <summary>
    /// Calculate where a manual Add step or Insert before operation goes.
    /// A missing selection means append for Add, prepend for Insert before.
    /// </summary>
    public static int GetInsertionIndex(
        int stepCount, int? selectedIndex, bool before)
    {
        if (stepCount < 0 || stepCount >= MacroRecordingSession.MaximumSteps)
            throw new ArgumentOutOfRangeException(nameof(stepCount),
                $"Sequence must have fewer than {MacroRecordingSession.MaximumSteps} steps.");
        if (selectedIndex.HasValue &&
            (selectedIndex.Value < 0 || selectedIndex.Value >= stepCount))
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        return selectedIndex.HasValue
            ? selectedIndex.Value + (before ? 0 : 1)
            : (before ? 0 : stepCount);
    }

    /// <summary>
    /// Set the DelayAfterMs of all matching keyboard press or release events.
    /// In particular, a KeyDown delay controls held-key duration until KeyUp,
    /// while a KeyUp delay controls spacing before the next step.
    /// </summary>
    public static int ApplyKeyboardDelay(
        IList<MacroStep> steps, MacroStepKind action, int delayMs)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (action is not (MacroStepKind.KeyDown or MacroStepKind.KeyUp))
            throw new ArgumentException(
                "Only KeyDown and KeyUp events support this standardization.",
                nameof(action));
        if (delayMs is < 0 or > 60_000)
            throw new ArgumentOutOfRangeException(nameof(delayMs));

        int count = 0;
        foreach (MacroStep step in steps)
        {
            if (step.Kind != action) continue;
            step.DelayAfterMs = delayMs;
            count++;
        }
        return count;
    }
}
