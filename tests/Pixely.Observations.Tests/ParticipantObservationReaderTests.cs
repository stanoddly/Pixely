namespace Pixely.Observations.Tests;

public readonly record struct ParticipantEntry(string Perceiver, int Value) : IObservationParticipation;

public static class Participants
{
    public static readonly string One = Base40Encoding.Encode(1);
    public static readonly string Two = Base40Encoding.Encode(2);
}

[TestFixture]
public sealed class ParticipantObservationReaderTests
{
    [Test]
    public void Reader_ReadsOnlyWhatItsParticipantPerceived()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry> reader = new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "reader");

        writer.Append(new ParticipantEntry(Participants.One, 10));
        writer.Append(new ParticipantEntry(Participants.Two, 20));
        writer.Append(new ParticipantEntry(Participants.One, 30));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 10, 30 }));
        Assert.That(reader.TryRead(out _), Is.False);
    }

    [Test]
    public void Readers_BoundToDifferentParticipantsEachSeeTheirOwn()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry> first = new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "first");
        ParticipantObservationReader<ParticipantEntry> second = new ParticipantObservationReader<ParticipantEntry>(log, Participants.Two, "second");

        writer.Append(new ParticipantEntry(Participants.One, 10));
        writer.Append(new ParticipantEntry(Participants.Two, 20));

        Assert.That(Drain(first), Is.EqualTo(new[] { 10 }));
        Assert.That(Drain(second), Is.EqualTo(new[] { 20 }));
    }

    [Test]
    public void Reader_PassingOverAnotherParticipantStillLetsTheLogTrim()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(8);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry> reader = new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "quiet");

        // Nothing here is this reader's, and draining it still has to free every slot or the log fills.
        for (int i = 0; i < 8 * 10; i++)
        {
            writer.Append(new ParticipantEntry(Participants.Two, i));
            Assert.That(reader.TryRead(out _), Is.False);
        }

        Assert.Pass();
    }

    [Test]
    public void Reader_OnlySeesEntriesAppendedAfterItsCreation()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        writer.Append(new ParticipantEntry(Participants.One, 10));

        ParticipantObservationReader<ParticipantEntry> reader = new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "reader");
        writer.Append(new ParticipantEntry(Participants.One, 20));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 20 }));
    }

    [Test]
    public void Reader_WithNothingOfItsOwnReturnsFalseAndTheDefaultEntry()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry> reader = new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "reader");

        writer.Append(new ParticipantEntry(Participants.Two, 20));

        Assert.That(reader.TryRead(out ParticipantEntry entry), Is.False);
        Assert.That(entry, Is.EqualTo(default(ParticipantEntry)));
    }

    [Test]
    public void Append_NamesTheParticipantReaderThatStoppedDraining()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(4);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        _ = new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "presenter");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new ParticipantEntry(Participants.Two, i));
        }

        Assert.That(() => writer.Append(new ParticipantEntry(Participants.Two, 4)),
            Throws.InvalidOperationException.With.Message.Contains("presenter"));
    }

    [Test]
    public void DisposedReader_ThrowsOnReadAndFreesTheLogForTrimming()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(4);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry> reader = new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "leaving");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new ParticipantEntry(Participants.One, i));
        }

        reader.Dispose();

        Assert.That(() => reader.TryRead(out _), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => writer.Append(new ParticipantEntry(Participants.One, 4)), Throws.Nothing);
    }

    [Test]
    public void Reader_RejectsAParticipantThatIsNotBase40()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);

        Assert.That(() => new ParticipantObservationReader<ParticipantEntry>(log, "player one", "presenter"), Throws.ArgumentException);
        Assert.That(() => new ParticipantObservationReader<ParticipantEntry>(log, "1", "presenter"), Throws.ArgumentException);
    }

    [Test]
    public void Readers_OfOneConsumerForTwoParticipantsShareTheConsumerName()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);

        Assert.That(() =>
        {
            new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "brain");
            new ParticipantObservationReader<ParticipantEntry>(log, Participants.Two, "brain");
        }, Throws.Nothing);
        Assert.That(() => new ParticipantObservationReader<ParticipantEntry>(log, Participants.One, "brain"), Throws.InvalidOperationException);
    }

    private static int[] Drain(ParticipantObservationReader<ParticipantEntry> reader)
    {
        List<int> values = new();
        while (reader.TryRead(out ParticipantEntry entry))
        {
            values.Add(entry.Value);
        }

        return values.ToArray();
    }
}
