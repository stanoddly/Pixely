namespace Pixely.Messages.Tests;

public readonly record struct ParticipantMessage(int Perceiver, int Value) : IMessageParticipation<int>;

[TestFixture]
public sealed class ParticipantMessageReaderTests
{
    [Test]
    public void Reader_ReadsOnlyWhatItsParticipantPerceived()
    {
        Messages<ParticipantMessage> messages = new Messages<ParticipantMessage>();
        MessageWriter<ParticipantMessage> writer = new MessageWriter<ParticipantMessage>(messages);
        ParticipantMessageReader<ParticipantMessage, int> reader = CreateReader(messages, 1);

        writer.Write(new ParticipantMessage(1, 10));
        writer.Write(new ParticipantMessage(2, 20));
        writer.Write(new ParticipantMessage(1, 30));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 10, 30 }));
        Assert.That(reader.TryRead(out _), Is.False);
    }

    [Test]
    public void Readers_BoundToDifferentParticipantsEachSeeTheirOwn()
    {
        Messages<ParticipantMessage> messages = new Messages<ParticipantMessage>();
        MessageWriter<ParticipantMessage> writer = new MessageWriter<ParticipantMessage>(messages);
        ParticipantMessageReader<ParticipantMessage, int> first = CreateReader(messages, 1);
        ParticipantMessageReader<ParticipantMessage, int> second = CreateReader(messages, 2);

        writer.Write(new ParticipantMessage(1, 10));
        writer.Write(new ParticipantMessage(2, 20));

        Assert.That(Drain(first), Is.EqualTo(new[] { 10 }));
        Assert.That(Drain(second), Is.EqualTo(new[] { 20 }));
    }

    [Test]
    public void Reader_PassingOverAnotherParticipantKeepsUpWithTheFrames()
    {
        Messages<ParticipantMessage> messages = new Messages<ParticipantMessage>();
        MessageWriter<ParticipantMessage> writer = new MessageWriter<ParticipantMessage>(messages);
        ParticipantMessageReader<ParticipantMessage, int> reader = CreateReader(messages, 1);

        // Nothing here is this reader's, and reading still has to move past it or the reader misses messages.
        for (int frame = 0; frame < 10; frame++)
        {
            writer.Write(new ParticipantMessage(2, frame));
            Assert.That(reader.TryRead(out _), Is.False);
            ((IUpdatable)messages).Update();
        }

        writer.Write(new ParticipantMessage(1, 100));
        Assert.That(Drain(reader), Is.EqualTo(new[] { 100 }));
    }

    [Test]
    public void Reader_WithNothingOfItsOwnReturnsFalseAndTheDefaultMessage()
    {
        Messages<ParticipantMessage> messages = new Messages<ParticipantMessage>();
        MessageWriter<ParticipantMessage> writer = new MessageWriter<ParticipantMessage>(messages);
        ParticipantMessageReader<ParticipantMessage, int> reader = CreateReader(messages, 1);

        writer.Write(new ParticipantMessage(2, 20));

        Assert.That(reader.TryRead(out ParticipantMessage message), Is.False);
        Assert.That(message, Is.EqualTo(default(ParticipantMessage)));
    }

    private static ParticipantMessageReader<ParticipantMessage, int> CreateReader(Messages<ParticipantMessage> messages, int participant) =>
        new ParticipantMessageReader<ParticipantMessage, int>(new MessageReader<ParticipantMessage>(messages), participant);

    private static int[] Drain(ParticipantMessageReader<ParticipantMessage, int> reader)
    {
        List<int> values = new List<int>();
        while (reader.TryRead(out ParticipantMessage message))
        {
            values.Add(message.Value);
        }

        return values.ToArray();
    }
}
