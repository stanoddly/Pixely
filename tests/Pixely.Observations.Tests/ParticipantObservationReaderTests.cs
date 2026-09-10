namespace Pixely.Observations.Tests;

public readonly record struct ParticipantEntry(int Perceiver, int Value) : IObservationParticipation<int>;

[TestFixture]
public sealed class ParticipantObservationReaderTests
{
    [Test]
    public void Reader_ReadsOnlyWhatItsParticipantPerceived()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry, int> reader = new ParticipantObservationReader<ParticipantEntry, int>(log, 1);

        writer.Append(new ParticipantEntry(1, 10));
        writer.Append(new ParticipantEntry(2, 20));
        writer.Append(new ParticipantEntry(1, 30));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 10, 30 }));
        Assert.That(reader.TryRead(out _), Is.False);
    }

    [Test]
    public void Readers_BoundToDifferentParticipantsEachSeeTheirOwn()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry, int> first = new ParticipantObservationReader<ParticipantEntry, int>(log, 1, "first");
        ParticipantObservationReader<ParticipantEntry, int> second = new ParticipantObservationReader<ParticipantEntry, int>(log, 2, "second");

        writer.Append(new ParticipantEntry(1, 10));
        writer.Append(new ParticipantEntry(2, 20));

        Assert.That(Drain(first), Is.EqualTo(new[] { 10 }));
        Assert.That(Drain(second), Is.EqualTo(new[] { 20 }));
    }

    [Test]
    public void Reader_PassingOverAnotherParticipantStillLetsTheLogTrim()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(8);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry, int> reader = new ParticipantObservationReader<ParticipantEntry, int>(log, 1, "quiet");

        // Nothing here is this reader's, and draining it still has to free every slot or the log fills.
        for (int i = 0; i < 8 * 10; i++)
        {
            writer.Append(new ParticipantEntry(2, i));
            Assert.That(reader.TryRead(out _), Is.False);
        }

        Assert.Pass();
    }

    [Test]
    public void Reader_OnlySeesEntriesAppendedAfterItsCreation()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        writer.Append(new ParticipantEntry(1, 10));

        ParticipantObservationReader<ParticipantEntry, int> reader = new ParticipantObservationReader<ParticipantEntry, int>(log, 1);
        writer.Append(new ParticipantEntry(1, 20));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 20 }));
    }

    [Test]
    public void Reader_WithNothingOfItsOwnReturnsFalseAndTheDefaultEntry()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry, int> reader = new ParticipantObservationReader<ParticipantEntry, int>(log, 1);

        writer.Append(new ParticipantEntry(2, 20));

        Assert.That(reader.TryRead(out ParticipantEntry entry), Is.False);
        Assert.That(entry, Is.EqualTo(default(ParticipantEntry)));
    }

    [Test]
    public void Append_NamesTheParticipantReaderThatStoppedDraining()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(4);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        _ = new ParticipantObservationReader<ParticipantEntry, int>(log, 1, "presenter");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new ParticipantEntry(2, i));
        }

        Assert.That(() => writer.Append(new ParticipantEntry(2, 4)),
            Throws.InvalidOperationException.With.Message.Contains("presenter"));
    }

    [Test]
    public void DisposedReader_ThrowsOnReadAndFreesTheLogForTrimming()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(4);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry, int> reader = new ParticipantObservationReader<ParticipantEntry, int>(log, 1, "leaving");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new ParticipantEntry(1, i));
        }

        reader.Dispose();

        Assert.That(() => reader.TryRead(out _), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => writer.Append(new ParticipantEntry(1, 4)), Throws.Nothing);
    }

    private static int[] Drain(ParticipantObservationReader<ParticipantEntry, int> reader)
    {
        List<int> values = new();
        while (reader.TryRead(out ParticipantEntry entry))
        {
            values.Add(entry.Value);
        }

        return values.ToArray();
    }
}
