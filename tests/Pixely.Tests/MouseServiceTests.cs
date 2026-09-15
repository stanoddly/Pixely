using Pixely.Input;

namespace Pixely.Tests;

public sealed class MouseServiceTests
{
    private static readonly ViewScope _viewScope = new(7);

    [Test]
    public void IsInWindow_FollowsPresenceEventsAndHandlersSeeTheNewState()
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(_viewScope, 42));
        MouseService mouseService = new(windowRegistry);
        List<(string Event, bool IsInWindow)> events = new();
        mouseService.SubscribeWindowEnter(_viewScope, 0, _ => events.Add(("enter", mouseService.IsInWindow(_viewScope))));
        mouseService.SubscribeWindowLeave(_viewScope, 0, _ => events.Add(("leave", mouseService.IsInWindow(_viewScope))));
        bool isInWindowBefore = mouseService.IsInWindow(_viewScope);

        mouseService.OnMouseWindowPresenceEvent(_viewScope, true, 1);
        mouseService.OnMouseWindowPresenceEvent(_viewScope, true, 2);
        mouseService.OnMouseWindowPresenceEvent(_viewScope, false, 3);
        mouseService.OnMouseWindowPresenceEvent(_viewScope, false, 4);

        Assert.Multiple(() =>
        {
            Assert.That(isInWindowBefore, Is.False);
            Assert.That(events, Is.EqualTo(new[] { ("enter", true), ("leave", false) }));
            Assert.That(mouseService.IsInWindow(_viewScope), Is.False);
        });
    }

    [Test]
    public void IsInWindow_ForUnregisteredView_Throws()
    {
        MouseService mouseService = new(new WindowRegistry());

        Assert.Throws<InvalidOperationException>(() => mouseService.IsInWindow(new ViewScope(99)));
    }
}
