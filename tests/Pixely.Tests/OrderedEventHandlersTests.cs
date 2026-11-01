using Pixely.Input;

namespace Pixely.Tests;

public sealed class OrderedEventHandlersTests
{
    [Test]
    public void Invoke_OrdersHandlersByOrderAndStopsWhenConsumed()
    {
        OrderedEventHandlers<TestEventArgs> handlers = new();
        List<string> calls = new();
        handlers.Add(10, _ => calls.Add("late"));
        handlers.Add(-10, _ => calls.Add("early"));
        handlers.Add(0, eventArgs =>
        {
            calls.Add("consume");
            eventArgs.Consume();
        });

        handlers.Invoke(new TestEventArgs());

        Assert.That(calls, Is.EqualTo(new[] { "early", "consume" }));
    }

    [Test]
    public void Invoke_ResetsConsumedBeforeDispatch()
    {
        OrderedEventHandlers<TestEventArgs> handlers = new();
        TestEventArgs eventArgs = new();
        bool called = false;
        handlers.Add(0, _ => called = true);
        eventArgs.Consume();

        handlers.Invoke(eventArgs);

        Assert.Multiple(() =>
        {
            Assert.That(called, Is.True);
            Assert.That(eventArgs.Consumed, Is.False);
        });
    }

    private sealed class TestEventArgs : ConsumableInputEventArgs
    {
    }
}
