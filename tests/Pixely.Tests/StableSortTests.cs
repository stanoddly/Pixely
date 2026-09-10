namespace Pixely.Tests;

public sealed class StableSortTests
{
    private sealed record Item(int Key, int Tag);

    [Test]
    public void StableSort_OrdersByKey()
    {
        List<Item> items = [new Item(3, 0), new Item(1, 1), new Item(2, 2)];

        items.StableSort(static item => item.Key);

        Assert.That(items.Select(item => item.Tag), Is.EqualTo(new[] { 1, 2, 0 }));
    }

    [Test]
    public void StableSort_KeepsOriginalOrderForEqualKeys()
    {
        // Long enough that an unstable sort cannot pass by falling back to an insertion sort, and
        // reversed keys so the sort has to move nearly every element.
        List<Item> items = new();
        for (int i = 0; i < 200; i++)
        {
            items.Add(new Item(-(i / 4), i));
        }

        List<Item> expected = items.OrderBy(item => item.Key).ToList();

        items.StableSort(static item => item.Key);

        Assert.That(items, Is.EqualTo(expected));
    }

    [Test]
    public void StableSort_HandlesNegativeKeys()
    {
        List<Item> items = [new Item(int.MaxValue, 0), new Item(int.MinValue, 1), new Item(0, 2), new Item(-1, 3)];

        items.StableSort(static item => item.Key);

        Assert.That(items.Select(item => item.Tag), Is.EqualTo(new[] { 1, 3, 2, 0 }));
    }

    [Test]
    public void StableSort_SortingAnAlreadySortedListChangesNothing()
    {
        List<Item> items = [new Item(0, 0), new Item(0, 1), new Item(1, 2), new Item(1, 3)];
        List<Item> expected = new(items);

        items.StableSort(static item => item.Key);

        Assert.That(items, Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(1023)]
    [TestCase(1024)]
    [TestCase(1025)]
    public void StableSort_MatchesAStableReferenceSortAtAnyLength(int count)
    {
        // 1024 is where the key buffer moves off the stack, so the lengths around it are covered.
        Random random = new(count);
        List<Item> items = new();
        for (int i = 0; i < count; i++)
        {
            items.Add(new Item(random.Next(-5, 5), i));
        }

        List<Item> expected = items.OrderBy(item => item.Key).ToList();

        items.StableSort(static item => item.Key);

        Assert.That(items, Is.EqualTo(expected));
    }

    [Test]
    public void StableSort_SortsASpanInPlace()
    {
        Item[] items = [new Item(2, 0), new Item(1, 1), new Item(2, 2), new Item(1, 3)];

        items.AsSpan().StableSort(static item => item.Key);

        Assert.That(items.Select(item => item.Tag), Is.EqualTo(new[] { 1, 3, 0, 2 }));
    }
}
