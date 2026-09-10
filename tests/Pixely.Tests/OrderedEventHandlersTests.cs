using Pixely.Input;

namespace Pixely.Tests;

public sealed class OrderedEventHandlersTests
{
    private static readonly ViewScope _firstView = new(1);
    private static readonly ViewScope _secondView = new(2);

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
    public void Invoke_InvokesOnlyHandlersForMatchingViewInOrder()
    {
        ViewScopedOrderedEventHandlers<TestEventArgs> handlers = new();
        List<string> calls = new();
        handlers.Add(_secondView, 0, _ => calls.Add("second"));
        handlers.Add(_firstView, 10, _ => calls.Add("late"));
        handlers.Add(_firstView, -10, _ => calls.Add("early"));

        handlers.Invoke(_firstView, new TestEventArgs());

        Assert.That(calls, Is.EqualTo(new[] { "early", "late" }));
    }

    [Test]
    public void Invoke_ResetsConsumedBeforeDispatch()
    {
        ViewScopedOrderedEventHandlers<TestEventArgs> handlers = new();
        TestEventArgs eventArgs = new();
        bool called = false;
        handlers.Add(_firstView, 0, _ => called = true);
        eventArgs.Consume();

        handlers.Invoke(_firstView, eventArgs);

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
