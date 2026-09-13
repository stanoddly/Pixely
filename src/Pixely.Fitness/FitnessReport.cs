namespace Pixely.Fitness;

public sealed record FitnessResult(string Name, IReadOnlyList<string> Violations)
{
    public bool IsFit => Violations.Count == 0;

    public override string ToString()
    {
        return IsFit ? Name : $"{Name}{Environment.NewLine}{string.Join(Environment.NewLine, Violations.Select(violation => "  " + violation))}";
    }
}

/// <summary>
/// Every fitness function's outcome, so one assertion reports the whole drift at once.
/// </summary>
public sealed class FitnessReport
{
    internal FitnessReport(IReadOnlyList<FitnessResult> results)
    {
        Results = results;
    }

    public IReadOnlyList<FitnessResult> Results { get; }

    public bool IsFit => Results.All(result => result.IsFit);

    public IEnumerable<FitnessResult> Failures => Results.Where(result => !result.IsFit);

    public FitnessResult this[string name] => Results.Single(result => result.Name == name);

    public override string ToString()
    {
        return IsFit ? "fit" : string.Join(Environment.NewLine, Failures);
    }
}
