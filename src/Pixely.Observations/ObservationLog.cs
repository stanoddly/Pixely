using System.Runtime.CompilerServices;

namespace Pixely.Observations;

/// <summary>
/// An append-only log of value entries that readers drain at their own pace through their own cursors.
/// Appending never calls a reader. An entry is dropped once every cursor has passed it, so the log is bounded
/// by the slowest reader rather than by how long the run lasts.
/// </summary>
/// <typeparam name="TEntry">
/// The single entry type of this log. Carry several kinds of entry in one log by making this a tagged value
/// type; the log never looks inside it.
/// </typeparam>
public sealed class ObservationLog<TEntry> : IObservationLog<TEntry>, IObservationWriter<TEntry> where TEntry : struct
{
    private const int InitialCapacity = 16;

    private readonly int _maximumCapacity;
    private readonly List<ObservationCursor<TEntry>> _cursors = new();
    private TEntry[] _entries;
    private int _head;
    private int _count;
    private long _firstSequence;
    private long _nextSequence;

    /// <param name="maximumCapacity">
    /// How many entries the log may retain before <see cref="Append"/> throws. The buffer starts small and grows
    /// towards this bound, so it is a stall detector rather than a working size: pick a number far past any
    /// legitimate burst, knowing a slot costs the size of <typeparamref name="TEntry"/>.
    /// </param>
    public ObservationLog(int maximumCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCapacity, 1);
        _maximumCapacity = maximumCapacity;
        _entries = new TEntry[Math.Min(InitialCapacity, maximumCapacity)];
    }

    public void Append(in TEntry entry)
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

    public ObservationCursor<TEntry> CreateCursor(string name = "unnamed")
    {
        ObservationCursor<TEntry> cursor = new ObservationCursor<TEntry>(this, name, _nextSequence);
        _cursors.Add(cursor);
        return cursor;
    }

    internal bool TryRead(ObservationCursor<TEntry> cursor, out TEntry entry)
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

        // Only worth clearing when a slot can keep an object alive; the check folds away for the rest.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<TEntry>())
        {
            for (int i = 0; i < removeCount; i++)
            {
                _entries[PhysicalIndex(i)] = default;
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

        return slowest;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= _entries.Length)
        {
            return;
        }

        int newCapacity = _entries.Length * 2;
        while (newCapacity < requiredCapacity)
        {
            newCapacity *= 2;
        }

        TEntry[] newEntries = new TEntry[Math.Min(newCapacity, _maximumCapacity)];
        int untilWrap = Math.Min(_count, _entries.Length - _head);
        Array.Copy(_entries, _head, newEntries, 0, untilWrap);
        Array.Copy(_entries, 0, newEntries, untilWrap, _count - untilWrap);
        _entries = newEntries;
        _head = 0;
    }

    private int PhysicalIndex(int offset)
    {
        return (_head + offset) % _entries.Length;
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

        return $"Observation log reached its maximum capacity of {_maximumCapacity} entries. "
            + $"Cursor '{string.Join("', '", stalled)}' stopped draining {_nextSequence - slowest} entries ago.";
    }
}
