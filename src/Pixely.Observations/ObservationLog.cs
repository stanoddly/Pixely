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
/// <see cref="ObservationReader{TEntry}"/>, so neither role can do the other's job.
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
    private readonly List<ObservationReader<TEntry>> _readers = new();
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

    internal bool TryRead(ObservationReader<TEntry> reader, [MaybeNullWhen(false)] out TEntry entry)
    {
        int offset = checked((int)(reader.NextSequence - _firstSequence));
        if (offset >= _count)
        {
            entry = default;
            return false;
        }

        entry = _entries[PhysicalIndex(offset)];
        reader.NextSequence++;
        Trim();
        return true;
    }

    // A reader starts after the last appended entry, so it sees only what is appended from now on.
    internal void AddReader(ObservationReader<TEntry> reader)
    {
        reader.NextSequence = _nextSequence;
        _readers.Add(reader);
    }

    internal void RemoveReader(ObservationReader<TEntry> reader)
    {
        _readers.Remove(reader);
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
        foreach (ObservationReader<TEntry> reader in _readers)
        {
            if (reader.NextSequence < slowest)
            {
                slowest = reader.NextSequence;
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

    private int PhysicalIndex(int offset)
    {
        return (_head + offset) % _entries.Length;
    }

    private string DescribeOverflow()
    {
        long slowest = SlowestSequence();
        List<string> stalled = new();
        foreach (ObservationReader<TEntry> reader in _readers)
        {
            if (reader.NextSequence == slowest)
            {
                stalled.Add(reader.Name);
            }
        }

        return $"Observation log reached its maximum capacity of {_maximumCapacity} entries. "
            + $"Reader '{string.Join("', '", stalled)}' stopped draining {_nextSequence - slowest} entries ago.";
    }
}
