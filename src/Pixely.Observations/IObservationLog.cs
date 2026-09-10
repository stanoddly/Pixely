namespace Pixely.Observations;

/// <summary>
/// The read half of an <see cref="ObservationLog{TEntry}"/>. Inject it where a reader creates its own cursor
/// and drains it on its own cadence.
/// </summary>
public interface IObservationLog<TEntry>
{
    /// <summary>
    /// Creates a cursor positioned after the last appended entry, so it sees only what is appended from now on.
    /// The name identifies the cursor when a stalled reader fills the log, so give it the reader's own name.
    /// </summary>
    ObservationCursor<TEntry> CreateCursor(string name = "unnamed");
}
