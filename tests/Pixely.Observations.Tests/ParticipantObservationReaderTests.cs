namespace Pixely.Observations.Tests;

public readonly record struct ParticipantEntry(ParticipantId Perceiver, int Value) : IObservationParticipation<ParticipantId>;

public static class Participants
{
    public static readonly ParticipantId One = new ParticipantId(1);
    public static readonly ParticipantId Two = new ParticipantId(2);
}

[TestFixture]
public sealed class ParticipantObservationReaderTests
{
    [Test]
    public void Reader_ReadsOnlyWhatItsParticipantPerceived()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry, ParticipantId> reader = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "reader");

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
        ParticipantObservationReader<ParticipantEntry, ParticipantId> first = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "first");
        ParticipantObservationReader<ParticipantEntry, ParticipantId> second = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.Two, "second");

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
        ParticipantObservationReader<ParticipantEntry, ParticipantId> reader = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "quiet");

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

        ParticipantObservationReader<ParticipantEntry, ParticipantId> reader = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "reader");
        writer.Append(new ParticipantEntry(Participants.One, 20));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 20 }));
    }

    [Test]
    public void Reader_WithNothingOfItsOwnReturnsFalseAndTheDefaultEntry()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        ParticipantObservationReader<ParticipantEntry, ParticipantId> reader = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "reader");

        writer.Append(new ParticipantEntry(Participants.Two, 20));

        Assert.That(reader.TryRead(out ParticipantEntry entry), Is.False);
        Assert.That(entry, Is.EqualTo(default(ParticipantEntry)));
    }

    [Test]
    public void Append_NamesTheParticipantReaderThatStoppedDraining()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(4);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        _ = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "presenter");

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
        ParticipantObservationReader<ParticipantEntry, ParticipantId> reader = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "leaving");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new ParticipantEntry(Participants.One, i));
        }

        reader.Dispose();

        Assert.That(() => reader.TryRead(out _), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => writer.Append(new ParticipantEntry(Participants.One, 4)), Throws.Nothing);
    }

    [Test]
    public void ParticipantId_PrintsAsTwelveBase40CharactersAndRejectsWhatCannot()
    {
        Assert.That(new ParticipantId(1).ToString(), Is.EqualTo("000000000001"));
        Assert.That(() => new ParticipantId(ulong.MaxValue), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Reader_IsNamedAfterTheConsumerAndTheParticipant()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(4);
        ObservationWriter<ParticipantEntry> writer = new ObservationWriter<ParticipantEntry>(log);
        _ = new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "presenter");

        for (int i = 0; i < 4; i++)
        {
            writer.Append(new ParticipantEntry(Participants.Two, i));
        }

        Assert.That(() => writer.Append(new ParticipantEntry(Participants.Two, 4)),
            Throws.InvalidOperationException.With.Message.Contains("'presenter:000000000001'"));
    }

    [Test]
    public void Readers_OfOneConsumerForTwoParticipantsShareTheConsumerName()
    {
        ObservationLog<ParticipantEntry> log = new ObservationLog<ParticipantEntry>(64);

        Assert.That(() =>
        {
            new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "brain");
            new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.Two, "brain");
        }, Throws.Nothing);
        Assert.That(() => new ParticipantObservationReader<ParticipantEntry, ParticipantId>(log, Participants.One, "brain"), Throws.InvalidOperationException);
    }

    private static int[] Drain(ParticipantObservationReader<ParticipantEntry, ParticipantId> reader)
    {
        List<int> values = new();
        while (reader.TryRead(out ParticipantEntry entry))
        {
            values.Add(entry.Value);
        }

        return values.ToArray();
    }
}
