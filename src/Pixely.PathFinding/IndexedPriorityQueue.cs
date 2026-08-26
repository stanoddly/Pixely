using System.Numerics;

namespace Pixely.PathFinding;

internal sealed class IndexedPriorityQueue<TIndex, TPriority>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TPriority : unmanaged, INumber<TPriority>
{
    private const int Arity = 4;
    private HeapNode[] _heap = [];
    private TIndex[] _positions = [];
    private int _count;

    internal void EnsureNodeCapacity(int nodeCount)
    {
        if (_positions.Length < nodeCount)
        {
            _positions = new TIndex[nodeCount];
        }
    }

    internal void Clear()
    {
        for (int position = 0; position < _count; position++)
        {
            _positions[_heap[position].Offset] = TIndex.Zero;
        }

        _count = 0;
    }

    internal void EnqueueOrUpdate(int offset, TPriority priority)
    {
        TIndex encodedPosition = _positions[offset];
        if (encodedPosition == TIndex.Zero)
        {
            EnsureHeapCapacity();
            HeapNode node = new HeapNode(offset, priority);
            MoveUp(node, _count);
            _count++;
            return;
        }

        int position = int.CreateChecked(encodedPosition) - 1;
        HeapNode current = _heap[position];
        if (priority < current.Priority)
        {
            MoveUp(new HeapNode(offset, priority), position);
        }
        else if (priority > current.Priority)
        {
            MoveDown(new HeapNode(offset, priority), position);
        }
    }

    internal bool TryDequeue(out int offset)
    {
        if (_count == 0)
        {
            offset = 0;
            return false;
        }

        HeapNode root = _heap[0];
        offset = root.Offset;
        _positions[offset] = TIndex.Zero;
        _count--;
        if (_count > 0)
        {
            MoveDown(_heap[_count], 0);
        }

        return true;
    }

    private void EnsureHeapCapacity()
    {
        if (_count < _heap.Length)
        {
            return;
        }

        if (_heap.Length == Array.MaxLength)
        {
            throw new OutOfMemoryException();
        }

        long expandedCapacity = _heap.Length == 0 ? Arity : (long)_heap.Length * 2;
        int capacity = (int)Math.Min(expandedCapacity, Array.MaxLength);
        Array.Resize(ref _heap, capacity);
    }

    private void MoveUp(HeapNode node, int position)
    {
        while (position > 0)
        {
            int parentPosition = (position - 1) / Arity;
            HeapNode parent = _heap[parentPosition];
            if (node.Priority >= parent.Priority)
            {
                break;
            }

            _heap[position] = parent;
            _positions[parent.Offset] = TIndex.CreateChecked(position + 1);
            position = parentPosition;
        }

        _heap[position] = node;
        _positions[node.Offset] = TIndex.CreateChecked(position + 1);
    }

    private void MoveDown(HeapNode node, int position)
    {
        while (true)
        {
            int firstChildPosition = position * Arity + 1;
            if ((uint)firstChildPosition >= (uint)_count)
            {
                break;
            }

            int bestChildPosition = firstChildPosition;
            int childEnd = firstChildPosition + Math.Min(Arity, _count - firstChildPosition);
            for (int childPosition = firstChildPosition + 1; childPosition < childEnd; childPosition++)
            {
                if (_heap[childPosition].Priority < _heap[bestChildPosition].Priority)
                {
                    bestChildPosition = childPosition;
                }
            }

            HeapNode bestChild = _heap[bestChildPosition];
            if (node.Priority <= bestChild.Priority)
            {
                break;
            }

            _heap[position] = bestChild;
            _positions[bestChild.Offset] = TIndex.CreateChecked(position + 1);
            position = bestChildPosition;
        }

        _heap[position] = node;
        _positions[node.Offset] = TIndex.CreateChecked(position + 1);
    }

    private readonly record struct HeapNode(int Offset, TPriority Priority);
}
