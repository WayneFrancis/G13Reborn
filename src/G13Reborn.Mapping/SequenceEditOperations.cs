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
