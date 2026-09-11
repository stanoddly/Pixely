namespace Pixely.Observations.Tests;

[TestFixture]
public sealed class ObservationSnapshotTests
{
    [Test]
    public void Capture_HoldsWhatNoReaderHasPassedYet()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = new ObservationReader<TestEntry>(log, "presenter");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));
        reader.TryRead(out _);

        ObservationSnapshot<TestEntry> snapshot = ObservationSnapshot<TestEntry>.Capture(log);

        Assert.That(snapshot.Entries, Is.EqualTo(new[] { new TestEntry(2) }));
        Assert.That(snapshot.ReaderPositions["presenter"], Is.EqualTo(0));
    }

    [Test]
    public void Capture_RecordsWhereEachReaderGotTo()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> fast = new ObservationReader<TestEntry>(log, "fast");
        new ObservationReader<TestEntry>(log, "slow");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));
        Drain(fast);

        ObservationSnapshot<TestEntry> snapshot = ObservationSnapshot<TestEntry>.Capture(log);

        Assert.That(snapshot.ReaderPositions, Is.EquivalentTo(new Dictionary<string, int> { ["fast"] = 2, ["slow"] = 0 }));
    }

    [Test]
    public void Capture_LeavesTheRunItSavedUntouched()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = new ObservationReader<TestEntry>(log, "presenter");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));
        ObservationSnapshot<TestEntry> snapshot = ObservationSnapshot<TestEntry>.Capture(log);

        Assert.That(Drain(reader), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(snapshot.Entries, Has.Count.EqualTo(2));
    }

    [Test]
    public void Capture_OfADrainedLogHoldsNothing()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = new ObservationReader<TestEntry>(log, "presenter");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));
        Drain(reader);

        ObservationSnapshot<TestEntry> snapshot = ObservationSnapshot<TestEntry>.Capture(log);

        Assert.That(snapshot.Entries, Is.Empty);
        Assert.That(snapshot.ReaderPositions["presenter"], Is.EqualTo(0));
    }

    [Test]
    public void Capture_ReadsAWrappedBufferInOrder()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = new ObservationReader<TestEntry>(log, "presenter");

        // Ten drained entries push the head mid-buffer, so the retained ones wrap around the end.
        for (int i = 0; i < 10; i++)
        {
            writer.Append(new TestEntry(i));
            reader.TryRead(out _);
        }

        for (int i = 100; i < 112; i++)
        {
            writer.Append(new TestEntry(i));
        }

        ObservationSnapshot<TestEntry> snapshot = ObservationSnapshot<TestEntry>.Capture(log);

        Assert.That(snapshot.Entries.Select(entry => entry.Value), Is.EqualTo(Enumerable.Range(100, 12)));
        Assert.That(snapshot.ReaderPositions["presenter"], Is.EqualTo(0));
    }

    [Test]
    public void RestoredReader_ResumesWhereItStopped()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> reader = new ObservationReader<TestEntry>(log, "presenter");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));
        writer.Append(new TestEntry(3));
        reader.TryRead(out _);

        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(64, Roundtrip(ObservationSnapshot<TestEntry>.Capture(log)));

        // The consumer's load path is the same code as its first run.
        ObservationReader<TestEntry> restoredReader = new ObservationReader<TestEntry>(restored, "presenter");

        Assert.That(Drain(restoredReader), Is.EqualTo(new[] { 2, 3 }));
    }

    [Test]
    public void RestoredReaders_ResumeAtTheirOwnPositions()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(log);
        ObservationReader<TestEntry> fast = new ObservationReader<TestEntry>(log, "fast");
        new ObservationReader<TestEntry>(log, "slow");

        writer.Append(new TestEntry(1));
        writer.Append(new TestEntry(2));
        Drain(fast);

        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(64, ObservationSnapshot<TestEntry>.Capture(log));

        Assert.That(Drain(new ObservationReader<TestEntry>(restored, "fast")), Is.Empty);
        Assert.That(Drain(new ObservationReader<TestEntry>(restored, "slow")), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void RestoredLog_KeepsAppendingAfterWhatItHeld()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(64);
        new ObservationReader<TestEntry>(log, "presenter");
        new ObservationWriter<TestEntry>(log).Append(new TestEntry(1));

        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(64, ObservationSnapshot<TestEntry>.Capture(log));
        ObservationReader<TestEntry> restoredReader = new ObservationReader<TestEntry>(restored, "presenter");
        new ObservationWriter<TestEntry>(restored).Append(new TestEntry(2));

        Assert.That(Drain(restoredReader), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void RestoredLog_TrimsBehindItsRestoredReaders()
    {
        ObservationLog<TestEntry> log = new ObservationLog<TestEntry>(4);
        new ObservationReader<TestEntry>(log, "presenter");
        new ObservationWriter<TestEntry>(log).Append(new TestEntry(1));

        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(4, ObservationSnapshot<TestEntry>.Capture(log));
        ObservationReader<TestEntry> restoredReader = new ObservationReader<TestEntry>(restored, "presenter");
        ObservationWriter<TestEntry> restoredWriter = new ObservationWriter<TestEntry>(restored);

        // A restored log that kept the entries but not the trim point would fill after three more appends.
        for (int i = 0; i < 100; i++)
        {
            restoredWriter.Append(new TestEntry(i));
            restoredReader.TryRead(out _);
        }

        Assert.Pass();
    }

    [Test]
    public void Reader_TheSnapshotDoesNotKnowStartsAfterTheRestoredEntries()
    {
        ObservationSnapshot<TestEntry> snapshot = Snapshot(new[] { 1, 2 }, ("presenter", 0));
        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(64, snapshot);

        ObservationReader<TestEntry> latecomer = new ObservationReader<TestEntry>(restored, "latecomer");
        new ObservationWriter<TestEntry>(restored).Append(new TestEntry(3));

        Assert.That(Drain(latecomer), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void RestoredReader_KeepsItsPlaceUntilItIsConstructed()
    {
        ObservationSnapshot<TestEntry> snapshot = Snapshot(new[] { 1, 2 }, ("presenter", 0));
        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(64, snapshot);

        // Another reader drains everything first, which would trim the presenter's entries away if a position
        // held nothing back until its reader appeared.
        ObservationReader<TestEntry> latecomer = new ObservationReader<TestEntry>(restored, "latecomer");
        new ObservationWriter<TestEntry>(restored).Append(new TestEntry(3));
        Drain(latecomer);

        Assert.That(Drain(new ObservationReader<TestEntry>(restored, "presenter")), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void UnrestoredReader_FillsTheLogAndIsNamed()
    {
        ObservationSnapshot<TestEntry> snapshot = Snapshot(new[] { 1 }, ("presenter", 0));
        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(4, snapshot);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(restored);

        for (int i = 0; i < 3; i++)
        {
            writer.Append(new TestEntry(i));
        }

        Assert.That(() => writer.Append(new TestEntry(4)),
            Throws.InvalidOperationException.With.Message.Contains("presenter")
                .And.Message.Contains("never constructed"));
    }

    [Test]
    public void Capture_CarriesAReaderThatWasNeverConstructedIntoTheNextSave()
    {
        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(64, Snapshot(new[] { 1 }, ("presenter", 0)));

        ObservationSnapshot<TestEntry> snapshot = ObservationSnapshot<TestEntry>.Capture(restored);

        Assert.That(snapshot.ReaderPositions["presenter"], Is.EqualTo(0));
        Assert.That(snapshot.Entries, Is.EqualTo(new[] { new TestEntry(1) }));
    }

    [Test]
    public void DroppingAReaderFromTheSnapshot_LetsTheRestoredLogTrim()
    {
        ObservationSnapshot<TestEntry> snapshot = Snapshot(new[] { 1 }, ("gone-for-good", 0));
        ObservationSnapshot<TestEntry> withoutIt = snapshot with { ReaderPositions = new Dictionary<string, int>() };

        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(4, withoutIt);
        ObservationWriter<TestEntry> writer = new ObservationWriter<TestEntry>(restored);

        for (int i = 0; i < 100; i++)
        {
            writer.Append(new TestEntry(i));
        }

        Assert.Pass();
    }

    [Test]
    public void Restore_RejectsAPositionTheSnapshotDoesNotHold()
    {
        Assert.That(() => ObservationLog<TestEntry>.Restore(64, Snapshot(new[] { 1, 2 }, ("presenter", -1))),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Message.Contains("presenter"));
        Assert.That(() => ObservationLog<TestEntry>.Restore(64, Snapshot(new[] { 1, 2 }, ("presenter", 3))),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => ObservationLog<TestEntry>.Restore(64, Snapshot(new[] { 1, 2 }, ("presenter", 2))), Throws.Nothing);
    }

    [Test]
    public void Restore_RejectsASnapshotLargerThanTheMaximumCapacity()
    {
        Assert.That(() => ObservationLog<TestEntry>.Restore(1, Snapshot(new[] { 1, 2 })), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Restore_KeepsASnapshotLargerThanTheInitialCapacity()
    {
        ObservationSnapshot<TestEntry> snapshot = Snapshot(Enumerable.Range(0, 100).ToArray(), ("presenter", 0));
        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(1024, snapshot);

        Assert.That(Drain(new ObservationReader<TestEntry>(restored, "presenter")), Is.EqualTo(Enumerable.Range(0, 100)));
    }

    [Test]
    public void Capture_OfARestoredLogCountsFromItsOwnEntries()
    {
        ObservationLog<TestEntry> restored = ObservationLog<TestEntry>.Restore(64, Snapshot(new[] { 1, 2, 3 }, ("presenter", 1)));
        ObservationReader<TestEntry> reader = new ObservationReader<TestEntry>(restored, "presenter");
        reader.TryRead(out _);

        ObservationSnapshot<TestEntry> snapshot = ObservationSnapshot<TestEntry>.Capture(restored);

        // The entry the reader had read before the first save is gone, so its position is an offset into what is left.
        Assert.That(snapshot.Entries, Is.EqualTo(new[] { new TestEntry(3) }));
        Assert.That(snapshot.ReaderPositions["presenter"], Is.EqualTo(0));
    }

    [Test]
    public void RestoredParticipantReader_ResumesWhereItStopped()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry, int> reader = new ParticipantObservationReader<ParticipantEntry, int>(log, 1, "presenter");

        writer.Append(new ParticipantEntry(1, 10));
        writer.Append(new ParticipantEntry(2, 20));
        writer.Append(new ParticipantEntry(1, 30));
        reader.TryRead(out _);

        ObservationLog<ParticipantEntry> restored = ObservationLog<ParticipantEntry>.Restore(64, ObservationSnapshot<ParticipantEntry>.Capture(log));
        ParticipantObservationReader<ParticipantEntry, int> restoredReader =
            new ParticipantObservationReader<ParticipantEntry, int>(restored, 1, "presenter");

        Assert.That(restoredReader.TryRead(out ParticipantEntry entry), Is.True);
        Assert.That(entry.Value, Is.EqualTo(30));
        Assert.That(restoredReader.TryRead(out _), Is.False);
    }

    private static ObservationSnapshot<TestEntry> Snapshot(int[] values, params (string Name, int Position)[] readers)
    {
        return new ObservationSnapshot<TestEntry>(values.Select(value => new TestEntry(value)).ToArray(), readers.ToDictionary(reader => reader.Name, reader => reader.Position));
    }

    // Serializing the snapshot is the game's job, so this stands in for whatever serializer it uses.
    private static ObservationSnapshot<TestEntry> Roundtrip(ObservationSnapshot<TestEntry> snapshot)
    {
        return new ObservationSnapshot<TestEntry>(snapshot.Entries.ToArray(), snapshot.ReaderPositions.ToDictionary(position => position.Key, position => position.Value));
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
