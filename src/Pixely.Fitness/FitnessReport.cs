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
/// Every fitness function's outcome, so one assertion reports the whole drift at once. Reports from
/// several rule sets merge into one.
/// </summary>
public sealed class FitnessReport
{
    public FitnessReport(IReadOnlyList<FitnessResult> results)
    {
        Results = results;
    }

    public static FitnessReport Merge(params IEnumerable<FitnessReport> reports)
    {
        return new FitnessReport(reports.SelectMany(report => report.Results).ToArray());
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
