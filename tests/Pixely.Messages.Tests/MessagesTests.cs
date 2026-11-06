using System.Runtime.CompilerServices;

namespace Pixely.Messages.Tests;

public readonly record struct TestMessage(int Value);

public sealed record TestReferenceMessage(int Value);

[TestFixture]
public sealed class MessagesTests
{
    [Test]
    public void Reader_ReadsWrittenMessagesInOrder()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        MessageReader<TestMessage> reader = new MessageReader<TestMessage>(messages);

        writer.Write(new TestMessage(1));
        writer.Write(new TestMessage(2));
        writer.Write(new TestMessage(3));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(reader.TryRead(out _), Is.False);
    }

    [Test]
    public void Reader_OnlySeesMessagesWrittenAfterItsCreation()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        writer.Write(new TestMessage(1));
        EndFrame(messages);
        writer.Write(new TestMessage(2));

        MessageReader<TestMessage> reader = new MessageReader<TestMessage>(messages);
        writer.Write(new TestMessage(3));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Readers_ReadIndependently()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        MessageReader<TestMessage> first = new MessageReader<TestMessage>(messages);
        MessageReader<TestMessage> second = new MessageReader<TestMessage>(messages);

        writer.Write(new TestMessage(1));
        Assert.That(Drain(first), Is.EqualTo(new[] { 1 }));

        writer.Write(new TestMessage(2));
        Assert.That(Drain(second), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(Drain(first), Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void Reader_ReadsInTheNextFrameWhatWasWrittenAfterItReadThisFrame()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        MessageReader<TestMessage> reader = new MessageReader<TestMessage>(messages);

        Assert.That(Drain(reader), Is.Empty);
        writer.Write(new TestMessage(1));
        EndFrame(messages);

        Assert.That(Drain(reader), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Reader_ReadingOncePerFrameMissesNothing()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        MessageReader<TestMessage> beforeWriter = new MessageReader<TestMessage>(messages);
        MessageReader<TestMessage> afterWriter = new MessageReader<TestMessage>(messages);
        List<int> readBefore = new List<int>();
        List<int> readAfter = new List<int>();

        for (int frame = 0; frame < 10; frame++)
        {
            readBefore.AddRange(Drain(beforeWriter));
            writer.Write(new TestMessage(frame * 2));
            writer.Write(new TestMessage(frame * 2 + 1));
            readAfter.AddRange(Drain(afterWriter));
            EndFrame(messages);
        }

        readBefore.AddRange(Drain(beforeWriter));
        Assert.That(readBefore, Is.EqualTo(Enumerable.Range(0, 20)));
        Assert.That(readAfter, Is.EqualTo(Enumerable.Range(0, 20)));
    }

    [Test]
    public void Reader_ThrowsAfterSkippingAWholeFrame()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        MessageReader<TestMessage> reader = new MessageReader<TestMessage>(messages);

        writer.Write(new TestMessage(1));
        writer.Write(new TestMessage(2));
        EndFrame(messages);
        EndFrame(messages);

        Assert.That(() => reader.TryRead(out _),
            Throws.InvalidOperationException.With.Message.Contains(nameof(TestMessage)).And.Message.Contains("missed 2 of its messages"));
    }

    [Test]
    public void Reader_ContinuesFromTheOldestKeptMessageAfterItThrew()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        MessageReader<TestMessage> reader = new MessageReader<TestMessage>(messages);

        writer.Write(new TestMessage(1));
        EndFrame(messages);
        writer.Write(new TestMessage(2));
        EndFrame(messages);
        writer.Write(new TestMessage(3));

        Assert.That(() => reader.TryRead(out _), Throws.InvalidOperationException);
        Assert.That(Drain(reader), Is.EqualTo(new[] { 2, 3 }));
    }

    [Test]
    public void Reader_BeforeMaintenanceReadsAMessageWrittenAfterMaintenanceInTheNextFrame()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        MessageReader<TestMessage> reader = new MessageReader<TestMessage>(messages);

        Assert.That(Drain(reader), Is.Empty);
        EndFrame(messages);
        // Rendering runs after Maintenance, so this is a write while frame 1 renders.
        writer.Write(new TestMessage(1));

        Assert.That(Drain(reader), Is.EqualTo(new[] { 1 }));
        EndFrame(messages);
        Assert.That(Drain(reader), Is.Empty);
    }

    [Test]
    public void Reader_ThatMovesItsReadFromBeforeMaintenanceToAfterItMissesMessages()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageWriter<TestMessage> writer = new MessageWriter<TestMessage>(messages);
        MessageReader<TestMessage> reader = new MessageReader<TestMessage>(messages);

        // Frame 1 reads before a later system writes. Frame 2 reads only after Maintenance, which has already dropped the message.
        Assert.That(Drain(reader), Is.Empty);
        writer.Write(new TestMessage(1));
        EndFrame(messages);
        EndFrame(messages);

        Assert.That(() => reader.TryRead(out _), Throws.InvalidOperationException.With.Message.Contains("missed 1 of its messages"));
    }

    [Test]
    public void Reader_WithNothingToReadReturnsFalseAndTheDefaultMessage()
    {
        Messages<TestMessage> messages = new Messages<TestMessage>();
        MessageReader<TestMessage> reader = new MessageReader<TestMessage>(messages);

        Assert.That(reader.TryRead(out TestMessage message), Is.False);
        Assert.That(message, Is.EqualTo(default(TestMessage)));
    }

    [Test]
    public void ReferenceMessage_ReadsBackTheWrittenInstancesInOrder()
    {
        Messages<TestReferenceMessage> messages = new Messages<TestReferenceMessage>();
        MessageWriter<TestReferenceMessage> writer = new MessageWriter<TestReferenceMessage>(messages);
        MessageReader<TestReferenceMessage> reader = new MessageReader<TestReferenceMessage>(messages);
        TestReferenceMessage first = new TestReferenceMessage(1);
        TestReferenceMessage second = new TestReferenceMessage(2);

        writer.Write(first);
        writer.Write(second);

        Assert.That(reader.TryRead(out TestReferenceMessage? read), Is.True);
        Assert.That(read, Is.SameAs(first));
        Assert.That(reader.TryRead(out read), Is.True);
        Assert.That(read, Is.SameAs(second));
    }

    [Test]
    public void Update_ReleasesAReferenceMessageAfterTwoFrames()
    {
        Messages<TestReferenceMessage> messages = new Messages<TestReferenceMessage>();

        WeakReference reference = WriteOne(messages);
        EndFrame(messages);
        EndFrame(messages);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(reference.IsAlive, Is.False);
    }

    // The only strong reference lives in this frame, so returning drops it and leaves the buffer as the one thing that
    // could still keep the message alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WriteOne(Messages<TestReferenceMessage> messages)
    {
        TestReferenceMessage message = new TestReferenceMessage(1);
        new MessageWriter<TestReferenceMessage>(messages).Write(message);
        return new WeakReference(message);
    }

    private static void EndFrame<TMessage>(Messages<TMessage> messages) => ((IUpdatable)messages).Update();

    private static int[] Drain(MessageReader<TestMessage> reader)
    {
        List<int> values = new List<int>();
        while (reader.TryRead(out TestMessage message))
        {
            values.Add(message.Value);
        }

        return values.ToArray();
    }
}
