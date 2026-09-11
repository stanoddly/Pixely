namespace Pixely.Observations;

/// <summary>
/// A ready-made participant identifier for a game that has no id type of its own to put in
/// <see cref="IObservationParticipation{TParticipantId}"/>. It prints as a fixed 12 character
/// <see cref="Base40Encoding"/> string, so it sits in a reader's name and in a save without escaping.
/// </summary>
public readonly record struct ParticipantId
{
    /// <param name="value">The participant's number, below 40^12.</param>
    public ParticipantId(ulong value)
    {
        // Encode is what enforces the bound, so a value that cannot print is rejected here rather than when a reader is named.
        Encoded = Base40Encoding.Encode(value);
        Value = value;
    }

    public ulong Value { get; }

    private string Encoded { get; }

    public override string ToString() => Encoded;
}
