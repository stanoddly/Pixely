using System.Runtime.InteropServices;

namespace Pixely;

/// <summary>
/// Sorts by an integer key without allocating. Items whose keys are equal keep the relative order
/// they already had, so a caller may sort the same list repeatedly without items drifting past each
/// other.
/// </summary>
public static class StableSortExtensions
{
    // A key is 8 bytes, so the cap is 8 KB of stack. Sorts larger than this are rare enough that
    // one array costs nothing.
    private const int StackKeyLimit = 1024;

    /// <summary>Sorts <paramref name="items"/> in place by <paramref name="orderKey"/>, lowest key first.</summary>
    public static void StableSort<T>(this List<T> items, Func<T, int> orderKey)
    {
        ArgumentNullException.ThrowIfNull(items);

        StableSort(CollectionsMarshal.AsSpan(items), orderKey);
    }

    /// <summary>Sorts <paramref name="items"/> in place by <paramref name="orderKey"/>, lowest key first.</summary>
    public static void StableSort<T>(this Span<T> items, Func<T, int> orderKey)
    {
        ArgumentNullException.ThrowIfNull(orderKey);

        if (items.Length < 2)
        {
            return;
        }

        // Only the keys are sorted, so the buffer holds no references and can sit on the stack even
        // when T does not.
        Span<SortKey> keys = items.Length <= StackKeyLimit
            ? stackalloc SortKey[items.Length]
            : new SortKey[items.Length];

        for (int i = 0; i < items.Length; i++)
        {
            keys[i] = new SortKey(orderKey(items[i]), i);
        }

        // The position makes every key unique, so there is no tie left for the sort to resolve
        // arbitrarily and an unstable algorithm still yields the one ordering the keys describe.
        keys.Sort();

        ApplyPermutation(items, keys);
    }

    private static void ApplyPermutation<T>(Span<T> items, Span<SortKey> keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            if (keys[i].Index < 0)
            {
                continue;
            }

            T held = items[i];
            int current = i;

            while (true)
            {
                int source = keys[current].Index;

                // A position is never negative, so its complement marks the slot as already moved.
                // The key half is dead once the sort has run.
                keys[current] = new SortKey(0, ~source);

                if (source == i)
                {
                    items[current] = held;
                    break;
                }

                items[current] = items[source];
                current = source;
            }
        }
    }

    private readonly record struct SortKey(int Key, int Index) : IComparable<SortKey>
    {
        public int CompareTo(SortKey other)
        {
            int result = Key.CompareTo(other.Key);
            return result != 0 ? result : Index.CompareTo(other.Index);
        }
    }
}
