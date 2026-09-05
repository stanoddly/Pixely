using System.Collections;

namespace Pixely.Ui;

/// <summary>
/// The children of an <see cref="Element"/>. Owns parent wiring and invalidation, so a child can
/// never end up in the tree without its parent knowing it has to re-measure.
/// </summary>
public sealed class ElementCollection : IReadOnlyList<Element>
{
    private readonly Element _owner;
    private readonly List<Element> _children = new();
    private readonly int _maxCount;

    internal ElementCollection(Element owner, int maxCount)
    {
        _owner = owner;
        _maxCount = maxCount;
    }

    public int Count => _children.Count;

    public Element this[int index] => _children[index];

    public void Add(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);
        Insert(_children.Count, child);
    }

    public void Insert(int index, Element child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (_children.Count >= _maxCount)
        {
            throw new InvalidOperationException(
                $"{_owner.GetType().Name} accepts at most {_maxCount} child element(s).");
        }

        if (child.Parent != null)
        {
            throw new InvalidOperationException("The element already has a parent; remove it from its current parent first.");
        }

        _children.Insert(index, child);
        child.Parent = _owner;

        // The whole attached subtree, not just the owner: what those elements last laid themselves
        // out for was their old place in a tree, and anything reading its root — a label's font —
        // would otherwise keep an answer computed under the old one. The walk reaches the owner and
        // its ancestors on the way up as well.
        child.InvalidateSubtreeMeasure();
    }

    public bool Remove(Element child)
    {
        ArgumentNullException.ThrowIfNull(child);

        int index = _children.IndexOf(child);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index)
    {
        Element child = _children[index];
        _children.RemoveAt(index);
        child.Parent = null;
        _owner.InvalidateMeasure();
    }

    public void Clear()
    {
        if (_children.Count == 0)
        {
            return;
        }

        foreach (Element child in _children)
        {
            child.Parent = null;
        }

        _children.Clear();
        _owner.InvalidateMeasure();
    }

    public List<Element>.Enumerator GetEnumerator() => _children.GetEnumerator();

    IEnumerator<Element> IEnumerable<Element>.GetEnumerator() => _children.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _children.GetEnumerator();
}
