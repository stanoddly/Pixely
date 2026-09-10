namespace Pixely.Observations;

/// <summary>
/// A whole <see cref="ObservationLog{TEntry}"/> as data: the entries no reader has passed yet, the sequence
/// number of the oldest of them, and where each reader had got to. Capture it to save a run, write it with the
/// game's own serializer, and hand it back to <see cref="ObservationLog{TEntry}.Restore"/> on load.
/// </summary>
/// <remarks>
/// A restored log puts each reader back where it stopped as the reader is constructed, matching it by name, so
/// a consumer's load path is the same code as its first run.
/// </remarks>
/// <param name="FirstSequence">The sequence number of the first entry; the entry at index i is FirstSequence + i.</param>
/// <param name="Entries">The retained entries, oldest first.</param>
/// <param name="ReaderPositions">
/// The sequence number each reader would have read next, by reader name. A name the restored log is never
/// asked for holds the trim point, the same as a reader that stopped draining: drop it from here when the
/// consumer it belonged to is gone for good.
/// </param>
public sealed record ObservationSnapshot<TEntry>(long FirstSequence, IReadOnlyList<TEntry> Entries, IReadOnlyDictionary<string, long> ReaderPositions)
{
    /// <summary>
    /// Copies out what the log holds. It consumes nothing and moves no reader, so a save leaves the run it
    /// saved untouched.
    /// </summary>
    public static ObservationSnapshot<TEntry> Capture(ObservationLog<TEntry> log) => new(log.FirstSequence, log.CopyRetainedEntries(), log.CopyReaderPositions());
}
