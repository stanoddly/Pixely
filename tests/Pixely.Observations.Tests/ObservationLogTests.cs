using System.Runtime.CompilerServices;

namespace Pixely.Observations.Tests;

public readonly record struct TestEntry(int Value);

public sealed record TestReferenceEntry(int Value);

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

    [Test]
    public void ReferenceEntry_ReadsBackTheAppendedInstancesInOrder()
    {
        ObservationLog<TestReferenceEntry> log = new ObservationLog<TestReferenceEntry>(64);
        ObservationCursor<TestReferenceEntry> cursor = log.CreateCursor();
        TestReferenceEntry first = new TestReferenceEntry(1);
        TestReferenceEntry second = new TestReferenceEntry(2);

        log.Append(first);
        log.Append(second);

        Assert.That(cursor.TryRead(out TestReferenceEntry? read), Is.True);
        Assert.That(read, Is.SameAs(first));
        Assert.That(cursor.TryRead(out read), Is.True);
        Assert.That(read, Is.SameAs(second));
    }

    [Test]
    public void ReferenceEntry_OnEmptyLogReturnsFalseAndNull()
    {
        ObservationLog<TestReferenceEntry> log = new ObservationLog<TestReferenceEntry>(64);
        ObservationCursor<TestReferenceEntry> cursor = log.CreateCursor();

        Assert.That(cursor.TryRead(out TestReferenceEntry? entry), Is.False);
        Assert.That(entry, Is.Null);
    }

    [Test]
    public void Buffer_GrowsWhileWrappedPreservingOrder()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(1024);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();

        // Drain ten entries first so the head sits mid-buffer and the retained entries wrap around the end,
        // which is what makes growth copy them in two segments.
        for (int i = 0; i < 10; i++)
        {
            log.Append(new TestEntry(i));
            cursor.TryRead(out _);
        }

        int[] expected = Enumerable.Range(100, 20).ToArray();
        foreach (int value in expected)
        {
            log.Append(new TestEntry(value));
        }

        Assert.That(Drain(cursor), Is.EqualTo(expected));
    }

    [Test]
    public void DisposingOneCursor_KeepsTheEntriesAnotherStillNeeds()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationCursor<TestEntry> leaving = log.CreateCursor("leaving");
        ObservationCursor<TestEntry> staying = log.CreateCursor("staying");

        log.Append(new TestEntry(1));
        log.Append(new TestEntry(2));
        leaving.Dispose();

        Assert.That(Drain(staying), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void DisposingACursorTwice_IsHarmless()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationCursor<TestEntry> cursor = log.CreateCursor();
        cursor.Dispose();

        Assert.That(() => cursor.Dispose(), Throws.Nothing);
    }

    [Test]
    public void Append_NamesEveryCursorTiedAtTheBack()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);
        log.CreateCursor("presenter");
        log.CreateCursor("audio");

        for (int i = 0; i < 4; i++)
        {
            log.Append(new TestEntry(i));
        }

        Assert.That(() => log.Append(new TestEntry(4)),
            Throws.InvalidOperationException.With.Message.Contains("presenter")
                .And.Message.Contains("audio"));
    }

    [Test]
    public void Append_SucceedsAgainOnceTheStalledCursorDrains()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);
        ObservationCursor<TestEntry> stalled = log.CreateCursor("stalled");

        for (int i = 0; i < 4; i++)
        {
            log.Append(new TestEntry(i));
        }

        Assert.That(() => log.Append(new TestEntry(4)), Throws.InvalidOperationException);
        Assert.That(Drain(stalled), Is.EqualTo(new[] { 0, 1, 2, 3 }));

        log.Append(new TestEntry(5));

        Assert.That(Drain(stalled), Is.EqualTo(new[] { 5 }));
    }

    [Test]
    public void Trimming_ReleasesAReferenceEntryOnceEveryCursorHasPassedIt()
    {
        ObservationLog<TestReferenceEntry> log = new ObservationLog<TestReferenceEntry>(64);
        ObservationCursor<TestReferenceEntry> cursor = log.CreateCursor();

        WeakReference reference = AppendAndDrainOne(log, cursor);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(reference.IsAlive, Is.False);
    }

    // The only strong reference lives in this frame, so returning drops it and leaves the log's slot as the
    // one thing that could still keep the entry alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AppendAndDrainOne(ObservationLog<TestReferenceEntry> log, ObservationCursor<TestReferenceEntry> cursor)
    {
        TestReferenceEntry entry = new TestReferenceEntry(1);
        log.Append(entry);
        cursor.TryRead(out TestReferenceEntry? _);
        return new WeakReference(entry);
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
