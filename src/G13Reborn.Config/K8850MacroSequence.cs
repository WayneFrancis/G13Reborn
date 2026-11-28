namespace G13Reborn.Config;

internal sealed class K8850MacroSequence
{
    public const int MaxTokens = 18;
    private readonly List<string> _steps = new();
    public IReadOnlyList<string> Steps => _steps;
    public int TokenCount => _steps.Sum(CountStep);
    public string Binding => string.Join(",", _steps);

    public static K8850MacroSequence Parse(string? binding)
    {
        var sequence = new K8850MacroSequence();
        if (string.IsNullOrWhiteSpace(binding)) return sequence;
        foreach (var step in binding.Split(','))
            sequence.Insert(sequence.Steps.Count, step);
        return sequence;
    }

    public static int CountStep(string step)
    {
        if (step.Contains(',') || step.TrimStart().StartsWith("hex:", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Use one named keyboard shortcut per step.");
        var report = K8850Binding.Encode(1, 1, step);
        if (report[4] != 1) throw new FormatException("Only keyboard shortcuts can be combined in a hardware keyboard macro.");
        return report[6];
    }

    public void Insert(int index, string step)
    {
        step = step.Trim();
        if (TokenCount + CountStep(step) > MaxTokens)
            throw new InvalidOperationException("Macro exceeds the 18-token hardware limit.");
        _steps.Insert(index, step);
    }

    public void Replace(int index, string step)
    {
        step = step.Trim();
        if (TokenCount - CountStep(_steps[index]) + CountStep(step) > MaxTokens)
            throw new InvalidOperationException("Macro exceeds the 18-token hardware limit.");
        _steps[index] = step;
    }

    public void Remove(int index) => _steps.RemoveAt(index);

    public void Move(int index, int destination)
    {
        if ((uint)index >= (uint)_steps.Count || (uint)destination >= (uint)_steps.Count)
            throw new ArgumentOutOfRangeException(nameof(destination));
        var step = _steps[index];
        _steps.RemoveAt(index);
        _steps.Insert(destination, step);
    }
}
