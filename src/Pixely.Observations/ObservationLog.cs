using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pixely.Observations;

/// <summary>
/// An append-only log of entries that readers drain at their own pace. Appending never calls a reader. An entry
/// is dropped once every reader has passed it, so the log is bounded by the slowest reader rather than by how
/// long the run lasts.
/// </summary>
/// <remarks>
/// The log is storage and nothing else: it is reached through an <see cref="ObservationWriter{TEntry}"/> or an
/// <see cref="ObservationReader{TEntry}"/>, so neither role can do the other's job, and through an
/// <see cref="ObservationSnapshot{TEntry}"/> when a run is saved. It belongs to one frame
/// loop and is not thread safe: appending, reading and creating a reader must all happen on the same thread.
/// </remarks>
/// <typeparam name="TEntry">
/// The single entry type of this log; the log never looks inside it. Carry several kinds of entry in one log
/// by making it a tagged type. A value type keeps appending free of allocation, which is why the entries a
/// game appends every frame should be one.
/// </typeparam>
public sealed class ObservationLog<TEntry>
{
    private const int InitialCapacity = 16;

    private readonly int _maximumCapacity;
    private readonly List<ObservationCursor<TEntry>> _cursors = new();
    private readonly Dictionary<string, long> _restoredPositions = new();
    private TEntry[] _entries;
    private int _head;
    private int _count;
    private long _firstSequence;
    private long _nextSequence;

    /// <param name="maximumCapacity">
    /// How many entries the log may retain before appending throws. The buffer starts small and grows towards
    /// this bound, so it is a stall detector rather than a working size: pick a number far past any legitimate
    /// burst, knowing a slot costs the size of <typeparamref name="TEntry"/>.
    /// </param>
    public ObservationLog(int maximumCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCapacity, 1);
        _maximumCapacity = maximumCapacity;
        _entries = new TEntry[Math.Min(InitialCapacity, maximumCapacity)];
    }

    private ObservationLog(int maximumCapacity, ObservationSnapshot<TEntry> snapshot)
    {
        _maximumCapacity = maximumCapacity;
        _entries = new TEntry[Math.Max(Math.Min(InitialCapacity, maximumCapacity), snapshot.Entries.Count)];
        for (int i = 0; i < snapshot.Entries.Count; i++)
        {
            _entries[i] = snapshot.Entries[i];
        }

        // Sequence numbers never leave the log, so a restored one counts from zero and a saved position, an
        // offset into the saved entries, is already a sequence number.
        _count = snapshot.Entries.Count;
        _nextSequence = snapshot.Entries.Count;
        foreach (KeyValuePair<string, int> position in snapshot.ReaderPositions)
        {
            _restoredPositions.Add(position.Key, position.Value);
        }
    }

    /// <summary>
    /// Restores a log holding what <see cref="ObservationSnapshot{TEntry}.Capture"/> took from a saved run.
    /// Each reader goes back to where it stopped as it is created, matched by name, so a consumer creates its
    /// reader exactly as it does on a first run.
    /// </summary>
    /// <param name="maximumCapacity">
    /// As for the constructor, and at least as large as the snapshot. Lowering it below what a saved run held
    /// is what makes a load throw rather than silently drop entries.
    /// </param>
    public static ObservationLog<TEntry> Restore(int maximumCapacity, ObservationSnapshot<TEntry> snapshot)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCapacity, 1);
        if (snapshot.Entries.Count > maximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot),
                $"The snapshot holds {snapshot.Entries.Count} entries, more than the maximum capacity of {maximumCapacity}.");
        }

        foreach (KeyValuePair<string, int> position in snapshot.ReaderPositions)
        {
            if (position.Value < 0 || position.Value > snapshot.Entries.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(snapshot),
                    $"The snapshot holds {snapshot.Entries.Count} entries, so reader '{position.Key}' cannot have read {position.Value} of them.");
            }
        }

        return new ObservationLog<TEntry>(maximumCapacity, snapshot);
    }

    internal void Append(in TEntry entry)
    {
        if (_count == _maximumCapacity)
        {
            throw new InvalidOperationException(DescribeOverflow());
        }

        EnsureCapacity(_count + 1);
        _entries[PhysicalIndex(_count)] = entry;
        _count++;
        _nextSequence++;
        Trim();
    }

    internal bool TryRead(ObservationCursor<TEntry> cursor, [MaybeNullWhen(false)] out TEntry entry)
    {
        int offset = checked((int)(cursor.NextSequence - _firstSequence));
        if (offset >= _count)
        {
            entry = default;
            return false;
        }

        entry = _entries[PhysicalIndex(offset)];
        cursor.NextSequence++;
        Trim();
        return true;
    }

    internal ObservationCursor<TEntry> CreateCursor(string name)
    {
        foreach (ObservationCursor<TEntry> existing in _cursors)
        {
            if (existing.Name == name)
            {
                throw new InvalidOperationException($"The log already has a reader named '{name}'. "
                    + "A name identifies a reader when the log fills and when a saved run is restored, so it has to be unique.");
            }
        }

        // A reader restored from a save resumes where it stopped; any other starts after the last appended
        // entry, so it sees only what is appended from now on.
        ObservationCursor<TEntry> cursor = new ObservationCursor<TEntry>(this, name);
        cursor.NextSequence = _restoredPositions.Remove(name, out long restored) ? restored : _nextSequence;
        _cursors.Add(cursor);
        return cursor;
    }

    internal TEntry[] CopyRetainedEntries()
    {
        TEntry[] entries = new TEntry[_count];
        for (int i = 0; i < _count; i++)
        {
            entries[i] = _entries[PhysicalIndex(i)];
        }

        return entries;
    }

    // A reader restored from an earlier save but not yet constructed still holds the log, so it belongs in the
    // next save as much as a reader that is here.
    internal Dictionary<string, int> CopyReaderPositions()
    {
        Dictionary<string, int> positions = new();
        foreach (KeyValuePair<string, long> restored in _restoredPositions)
        {
            positions.Add(restored.Key, checked((int)(restored.Value - _firstSequence)));
        }

        foreach (ObservationCursor<TEntry> cursor in _cursors)
        {
            positions.Add(cursor.Name, checked((int)(cursor.NextSequence - _firstSequence)));
        }

        return positions;
    }

    internal void RemoveCursor(ObservationCursor<TEntry> cursor)
    {
        _cursors.Remove(cursor);
        Trim();
    }

    private void Trim()
    {
        if (_count == 0)
        {
            return;
        }

        int removeCount = checked((int)(SlowestSequence() - _firstSequence));
        if (removeCount == 0)
        {
            return;
        }

        // Only worth clearing when a slot can keep an object alive; the check folds away for the rest. A cleared
        // slot is past every reader, so nothing reads it back before an append overwrites it.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<TEntry>())
        {
            for (int i = 0; i < removeCount; i++)
            {
                _entries[PhysicalIndex(i)] = default!;
            }
        }

        _head = PhysicalIndex(removeCount);
        _count -= removeCount;
        _firstSequence += removeCount;
    }

    private long SlowestSequence()
    {
        long slowest = _nextSequence;
        foreach (ObservationCursor<TEntry> cursor in _cursors)
        {
            if (cursor.NextSequence < slowest)
            {
                slowest = cursor.NextSequence;
            }
        }

        foreach (long position in _restoredPositions.Values)
        {
            if (position < slowest)
            {
                slowest = position;
            }
        }

        return slowest;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _entries.Length)
        {
            return;
        }

        // Doubling only while it stays under the bound, so the last step lands on the bound instead of overflowing.
        int newCapacity = _entries.Length <= _maximumCapacity / 2 ? _entries.Length * 2 : _maximumCapacity;
        TEntry[] newEntries = new TEntry[newCapacity];
        int untilWrap = Math.Min(_count, _entries.Length - _head);
        Array.Copy(_entries, _head, newEntries, 0, untilWrap);
        Array.Copy(_entries, 0, newEntries, untilWrap, _count - untilWrap);
        _entries = newEntries;
        _head = 0;
    }

    // Wrapping by subtraction rather than by modulo, so a buffer whose head plus offset would pass int range
    // still indexes correctly, and the hot path costs a compare instead of a division.
    private int PhysicalIndex(int offset)
    {
        int untilWrap = _entries.Length - _head;
        return offset < untilWrap ? _head + offset : offset - untilWrap;
    }

    private string DescribeOverflow()
    {
        long slowest = SlowestSequence();
        List<string> stalled = new();
        foreach (ObservationCursor<TEntry> cursor in _cursors)
        {
            if (cursor.NextSequence == slowest)
            {
                stalled.Add(cursor.Name);
            }
        }

        List<string> unrestored = new();
        foreach (KeyValuePair<string, long> position in _restoredPositions)
        {
            if (position.Value == slowest)
            {
                unrestored.Add(position.Key);
            }
        }

        string message = $"Observation log reached its maximum capacity of {_maximumCapacity} entries.";
        if (stalled.Count > 0)
        {
            message += $" Reader '{string.Join("', '", stalled)}' stopped draining {_nextSequence - slowest} entries ago.";
        }

        if (unrestored.Count > 0)
        {
            message += $" Reader '{string.Join("', '", unrestored)}' was restored from a save but never created.";
        }

        return message;
    }
}
