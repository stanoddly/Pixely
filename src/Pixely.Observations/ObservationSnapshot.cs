namespace Pixely.Observations;

/// <summary>
/// A whole <see cref="ObservationLog{TEntry}"/> as data: the entries no reader has passed yet and how far into
/// them each reader had got. Capture it to save a run, write it with the game's own serializer, and hand it back
/// to <see cref="ObservationLog{TEntry}.Restore"/> on load.
/// </summary>
/// <remarks>
/// A restored log puts each reader back where it stopped as the reader is created, matching it by name, so
/// a consumer's load path is the same code as its first run.
/// </remarks>
/// <param name="Entries">The retained entries, oldest first.</param>
/// <param name="ReaderPositions">
/// How many of <paramref name="Entries"/> each reader had already read, by reader name: 0 means none of them,
/// the count means all of them. A name no reader claims by the restored log's first append or read is dropped.
/// </param>
public sealed record ObservationSnapshot<TEntry>(IReadOnlyList<TEntry> Entries, IReadOnlyDictionary<string, int> ReaderPositions)
{
    /// <summary>
    /// Copies out what the log holds. It consumes nothing and moves no reader, so a save leaves the run it
    /// saved untouched.
    /// </summary>
    public static ObservationSnapshot<TEntry> Capture(ObservationLog<TEntry> log) => new(log.CopyRetainedEntries(), log.CopyReaderPositions());
}
