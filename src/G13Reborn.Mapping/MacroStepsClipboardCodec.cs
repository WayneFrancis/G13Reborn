using System.Text.Json;

namespace G13Reborn.Mapping;

/// <summary>
/// Typed JSON payload used in the Windows Clipboard's private
/// G13Reborn.MacroSteps.v1 format. No OS dependencies so malformed/foreign
/// clipboard content can be tested without interacting with the desktop.
/// </summary>
public static class MacroStepsClipboardCodec
{
    public const string Format = "G13Reborn.MacroSteps.v1";
    private const int MaximumPayloadCharacters = 100_000;

    public static string Serialize(IReadOnlyList<MacroStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count is < 1 or > MacroRecordingSession.MaximumSteps)
            throw new ArgumentException("Copy 1–200 steps.", nameof(steps));
        foreach (MacroStep step in steps) step.Validate();
        return JsonSerializer.Serialize(steps);
    }

    public static List<MacroStep> Deserialize(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0 || payload.Length > MaximumPayloadCharacters)
            throw new ArgumentException("Clipboard macro data is empty or too large.");

        try
        {
            List<MacroStep>? steps = JsonSerializer.Deserialize<List<MacroStep>>(payload);
            if (steps is null || steps.Count is < 1 or > MacroRecordingSession.MaximumSteps)
                throw new ArgumentException("Clipboard must contain 1–200 macro steps.");
            foreach (MacroStep? step in steps)
            {
                if (step is null)
                    throw new ArgumentException("Clipboard contains a null macro step.");
                step.Validate();
            }
            return steps.Select(s => s.Clone()).ToList();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Clipboard does not contain valid G13Reborn macro steps.", ex);
        }
    }
}
