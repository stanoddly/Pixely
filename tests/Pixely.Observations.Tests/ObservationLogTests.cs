using System.Runtime.CompilerServices;

namespace Pixely.Observations.Tests;

public readonly record struct TestEntry(int Value);

public sealed record TestReferenceEntry(int Value);

[TestFixture]
public sealed class ObservationLogTests
{
    [Test]
    public void Reader_ReadsAppendedEntriesInOrder()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));
        writer.Append(new TestEntry(3));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(reader.TryRead(out _), Is.False);
    }

    [Test]
    public void Reader_OnlySeesEntriesAppendedAfterItsCreation()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        writer.Append(new TestEntry(1));

        ObservationReader<TestEntry> reader = log.CreateReader("reader");
        writer.Append(new TestEntry(2));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void Readers_DrainIndependentlyAtTheirOwnPace()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> fast = log.CreateReader("fast");
        ObservationReader<TestEntry> slow = log.CreateReader("slow");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));

        Assert.That(Drain(fast), Is.EqualTo(new[] { 1, 2 }));

        writer.Append(new TestEntry(3));

        // The slow reader still sees everything from where it started.
        Assert.That(Drain(slow), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(Drain(fast), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Buffer_GrowsBeyondInitialCapacityPreservingOrder()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(1024);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");

        // Far beyond the initial capacity of 16, without draining, forcing growth.
        int[] expected = Enumerable.Range(0, 100).ToArray();
        foreach (int value in expected)
        {
            writer.Append(new TestEntry(value));
        }

        Assert.That(Drain(reader), Is.EqualTo(expected));
    }

    [Test]
    public void Buffer_GrowsWhileWrappedPreservingOrder()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(1024);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");

        // Drain ten entries first so the head sits mid-buffer and the retained entries wrap around the end,
        // which is what makes growth copy them in two segments.
        for (int i = 0; i < 10; i++)
        {
            writer.Append(new TestEntry(i));
            reader.TryRead(out _);
        }

        int[] expected = Enumerable.Range(100, 20).ToArray();
        foreach (int value in expected)
        {
            writer.Append(new TestEntry(value));
        }

        Assert.That(Drain(reader), Is.EqualTo(expected));
    }

    [Test]
    public void Buffer_GrowsNoFurtherThanTheMaximumCapacity()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(40);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");

        // Doubling from 16 would overshoot 40; the buffer has to stop at it and still keep order.
        int[] expected = Enumerable.Range(0, 40).ToArray();
        foreach (int value in expected)
        {
            writer.Append(new TestEntry(value));
        }

        Assert.That(Drain(reader), Is.EqualTo(expected));
    }

    [Test]
    public void MaximumCapacity_BelowTheInitialCapacityIsHonoured()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = log.CreateReader("stalled");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new TestEntry(i));
        }

        Assert.That(() => writer.Append(new TestEntry(4)), Throws.InvalidOperationException);
        Assert.That(Drain(reader), Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void MaximumCapacity_MustBeAtLeastOne()
    {
        Assert.That(() => new ObservationLog<TestEntry>(0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Buffer_WrapsAroundWhenInterleavingAppendAndRead()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");

        // Interleaving advances the head past the modulo boundary repeatedly.
        List<int> read = new();
        for (int i = 0; i < 100; i++)
        {
            writer.Append(new TestEntry(i));
            Assert.That(reader.TryRead(out TestEntry entry), Is.True);
            read.Add(entry.Value);
        }

        Assert.That(read, Is.EqualTo(Enumerable.Range(0, 100)));
    }

    [Test]
    public void Trimming_ReleasesEntriesOnceEveryReaderHasPassedThem()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(32);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");

        // With a single reader that keeps up, the log never fills no matter how many entries flow through it.
        for (int i = 0; i < 32 * 100; i++)
        {
            writer.Append(new TestEntry(i));
            reader.TryRead(out _);
        }

        Assert.Pass();
    }

    [Test]
    public void Trimming_KeepsNothingWhenNoReaderExists()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);

        // Nothing reads, so nothing is retained and the maximum capacity is never reached.
        for (int i = 0; i < 100; i++)
        {
            writer.Append(new TestEntry(i));
        }

        Assert.That(Drain(log.CreateReader("reader")), Is.Empty);
    }

    [Test]
    public void Append_ThrowsNamingTheReaderThatStoppedDraining()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(8);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> keepingUp = log.CreateReader("keeping-up");
        _ = log.CreateReader("presenter");

        for (int i = 0; i < 8; i++)
        {
            writer.Append(new TestEntry(i));
            keepingUp.TryRead(out _);
        }

        Assert.That(() => writer.Append(new TestEntry(8)),
            Throws.InvalidOperationException.With.Message.Contains("presenter")
                .And.Message.Contains("8 entries ago")
                .And.Message.Not.Contains("keeping-up"));
    }

    [Test]
    public void Append_NamesEveryReaderTiedAtTheBack()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        _ = log.CreateReader("presenter");
        _ = log.CreateReader("audio");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new TestEntry(i));
        }

        Assert.That(() => writer.Append(new TestEntry(4)),
            Throws.InvalidOperationException.With.Message.Contains("presenter")
                .And.Message.Contains("audio"));
    }

    [Test]
    public void Append_SucceedsAgainOnceTheStalledReaderDrains()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> stalled = log.CreateReader("stalled");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new TestEntry(i));
        }

        Assert.That(() => writer.Append(new TestEntry(4)), Throws.InvalidOperationException);
        Assert.That(Drain(stalled), Is.EqualTo(new[] { 0, 1, 2, 3 }));

        writer.Append(new TestEntry(5));

        Assert.That(Drain(stalled), Is.EqualTo(new[] { 5 }));
    }

    [Test]
    public void DisposingStalledReader_FreesTheLogForTrimming()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(8);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> stalled = log.CreateReader("stalled");

        for (int i = 0; i < 8; i++)
        {
            writer.Append(new TestEntry(i));
        }

        stalled.Dispose();

        Assert.That(() => writer.Append(new TestEntry(8)), Throws.Nothing);
    }

    [Test]
    public void DisposingOneReader_KeepsTheEntriesAnotherStillNeeds()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> leaving = log.CreateReader("leaving");
        ObservationReader<TestEntry> staying = log.CreateReader("staying");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));
        leaving.Dispose();

        Assert.That(Drain(staying), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void DisposingAReaderTwice_IsHarmless()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");
        reader.Dispose();

        Assert.That(() => reader.Dispose(), Throws.Nothing);
    }

    [Test]
    public void DisposedReader_ThrowsOnRead()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");
        reader.Dispose();

        Assert.That(() => reader.TryRead(out _), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Reader_OnEmptyLogReturnsFalseAndTheDefaultEntry()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationReader<TestEntry> reader = log.CreateReader("reader");

        Assert.That(reader.TryRead(out TestEntry entry), Is.False);
        Assert.That(entry, Is.EqualTo(default(TestEntry)));
    }

    [Test]
    public void ReferenceEntry_ReadsBackTheAppendedInstancesInOrder()
    {
        ObservationLog<TestReferenceEntry> log = new ObservationLog<TestReferenceEntry>(64);
        ObservationWriter<TestReferenceEntry> writer = new ObservationWriter<TestReferenceEntry>(log);
        ObservationReader<TestReferenceEntry> reader = log.CreateReader("reader");
        TestReferenceEntry first = new TestReferenceEntry(1);
        TestReferenceEntry second = new TestReferenceEntry(2);

        writer.Append(first);
        writer.Append(second);

        Assert.That(reader.TryRead(out TestReferenceEntry? read), Is.True);
        Assert.That(read, Is.SameAs(first));
        Assert.That(reader.TryRead(out read), Is.True);
        Assert.That(read, Is.SameAs(second));
    }

    [Test]
    public void ReferenceEntry_OnEmptyLogReturnsFalseAndNull()
    {
        ObservationLog<TestReferenceEntry> log = new ObservationLog<TestReferenceEntry>(64);
        ObservationReader<TestReferenceEntry> reader = log.CreateReader("reader");

        Assert.That(reader.TryRead(out TestReferenceEntry? entry), Is.False);
        Assert.That(entry, Is.Null);
    }

    [Test]
    public void Trimming_ReleasesAReferenceEntryOnceEveryReaderHasPassedIt()
    {
        ObservationLog<TestReferenceEntry> log = new ObservationLog<TestReferenceEntry>(64);
        ObservationReader<TestReferenceEntry> reader = log.CreateReader("reader");

        WeakReference reference = AppendAndDrainOne(log, reader);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(reference.IsAlive, Is.False);
    }

    // The only strong reference lives in this frame, so returning drops it and leaves the log's slot as the
    // one thing that could still keep the entry alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AppendAndDrainOne(ObservationLog<TestReferenceEntry> log, ObservationReader<TestReferenceEntry> reader)
    {
        TestReferenceEntry entry = new TestReferenceEntry(1);
        new ObservationWriter<TestReferenceEntry>(log).Append(entry);
        reader.TryRead(out TestReferenceEntry? _);
        return new WeakReference(entry);
    }

    [Test]
    public void Reader_NameMustBeUniqueWithinTheLog()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        log.CreateReader("presenter");

        Assert.That(() => log.CreateReader("presenter"),
            Throws.InvalidOperationException.With.Message.Contains("presenter"));
    }

    [Test]
    public void Reader_NameIsFreeAgainOnceTheReaderIsDisposed()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationReader<TestEntry> reader = log.CreateReader("presenter");
        reader.Dispose();

        Assert.That(() => log.CreateReader("presenter"), Throws.Nothing);
    }

    private static int[] Drain(ObservationReader<TestEntry> reader)
    {
        List<int> values = new();
        while (reader.TryRead(out TestEntry entry))
        {
            values.Add(entry.Value);
        }

        return values.ToArray();
    }
}
