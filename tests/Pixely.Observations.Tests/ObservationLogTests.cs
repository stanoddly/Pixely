namespace Pixely.Observations.Tests;

public readonly record struct TestEntry(int Value);

[TestFixture]
public sealed class ObservationLogTests
{
    [Test]
    public void Cursor_ReadsAppendedEntriesInOrder()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();

        log.Append(new TestEntry(1));
        log.Append(new TestEntry(2));
        log.Append(new TestEntry(3));

        Assert.That(Drain(cursor), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(cursor.TryRead(out _), Is.False);
    }

    [Test]
    public void Cursor_OnlySeesEntriesAppendedAfterItsCreation()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        log.Append(new TestEntry(1));

        ObservationCursor<TestEntry> cursor = log.CreateCursor();
        log.Append(new TestEntry(2));

        Assert.That(Drain(cursor), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void Cursors_DrainIndependentlyAtTheirOwnPace()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationCursor<TestEntry> fast = log.CreateCursor("fast");
        ObservationCursor<TestEntry> slow = log.CreateCursor("slow");

        log.Append(new TestEntry(1));
        log.Append(new TestEntry(2));

        Assert.That(Drain(fast), Is.EqualTo(new[] { 1, 2 }));

        log.Append(new TestEntry(3));

        // The slow cursor still sees everything from where it started.
        Assert.That(Drain(slow), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(Drain(fast), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Buffer_GrowsBeyondInitialCapacityPreservingOrder()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(1024);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();

        // Far beyond the initial capacity of 16, without draining, forcing growth.
        int[] expected = Enumerable.Range(0, 100).ToArray();
        foreach (int value in expected)
        {
            log.Append(new TestEntry(value));
        }

        Assert.That(Drain(cursor), Is.EqualTo(expected));
    }

    [Test]
    public void Buffer_GrowsNoFurtherThanTheMaximumCapacity()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(40);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();

        // Doubling from 16 would overshoot 40; the buffer has to stop at it and still keep order.
        int[] expected = Enumerable.Range(0, 40).ToArray();
        foreach (int value in expected)
        {
            log.Append(new TestEntry(value));
        }

        Assert.That(Drain(cursor), Is.EqualTo(expected));
    }

    [Test]
    public void MaximumCapacity_BelowTheInitialCapacityIsHonoured()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);
        ObservationCursor<TestEntry> cursor = log.CreateCursor("stalled");

        for (int i = 0; i < 4; i++)
        {
            log.Append(new TestEntry(i));
        }

        Assert.That(() => log.Append(new TestEntry(4)), Throws.InvalidOperationException);
        Assert.That(Drain(cursor), Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void Buffer_WrapsAroundWhenInterleavingAppendAndRead()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();

        // Interleaving advances the head past the modulo boundary repeatedly.
        List<int> read = new();
        for (int i = 0; i < 100; i++)
        {
            log.Append(new TestEntry(i));
            Assert.That(cursor.TryRead(out TestEntry entry), Is.True);
            read.Add(entry.Value);
        }

        Assert.That(read, Is.EqualTo(Enumerable.Range(0, 100)));
    }

    [Test]
    public void Trimming_ReleasesEntriesOnceEveryCursorHasPassedThem()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(32);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();

        // With a single cursor that keeps up, the log never fills no matter how many entries flow through it.
        for (int i = 0; i < 32 * 100; i++)
        {
            log.Append(new TestEntry(i));
            cursor.TryRead(out _);
        }

        Assert.Pass();
    }

    [Test]
    public void Trimming_KeepsNothingWhenNoCursorExists()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);

        // Nothing reads, so nothing is retained and the maximum capacity is never reached.
        for (int i = 0; i < 100; i++)
        {
            log.Append(new TestEntry(i));
        }

        Assert.That(Drain(log.CreateCursor()), Is.Empty);
    }

    [Test]
    public void Append_ThrowsNamingTheCursorThatStoppedDraining()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(8);
        ObservationCursor<TestEntry> keepingUp = log.CreateCursor("keeping-up");
        log.CreateCursor("presenter");

        for (int i = 0; i < 8; i++)
        {
            log.Append(new TestEntry(i));
            keepingUp.TryRead(out _);
        }

        Assert.That(() => log.Append(new TestEntry(8)),
            Throws.InvalidOperationException.With.Message.Contains("presenter")
                .And.Message.Contains("8 entries ago")
                .And.Message.Not.Contains("keeping-up"));
    }

    [Test]
    public void DisposingStalledCursor_FreesTheLogForTrimming()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(8);
        ObservationCursor<TestEntry> stalled = log.CreateCursor("stalled");

        for (int i = 0; i < 8; i++)
        {
            log.Append(new TestEntry(i));
        }

        stalled.Dispose();

        Assert.That(() => log.Append(new TestEntry(8)), Throws.Nothing);
    }

    [Test]
    public void DisposedCursor_ThrowsOnRead()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();
        cursor.Dispose();

        Assert.That(() => cursor.TryRead(out _), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Cursor_OnEmptyLogReturnsFalseAndTheDefaultEntry()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();

        Assert.That(cursor.TryRead(out TestEntry entry), Is.False);
        Assert.That(entry, Is.EqualTo(default(TestEntry)));
    }

    [Test]
    public void MaximumCapacity_MustBeAtLeastOne()
    {
        Assert.That(() => new ObservationLog<TestEntry>(0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static int[] Drain(ObservationCursor<TestEntry> cursor)
    {
        List<int> values = new();
        while (cursor.TryRead(out TestEntry entry))
        {
            values.Add(entry.Value);
        }

        return values.ToArray();
    }
}
