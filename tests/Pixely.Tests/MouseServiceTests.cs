using Pixely.Input;

namespace Pixely.Tests;

public sealed class MouseServiceTests
{
    [Test]
    public void IsInWindow_FollowsPresenceEventsAndHandlersSeeTheNewState()
    {
        MouseService mouseService = new();
        List<(string Event, bool IsInWindow)> events = new();
        mouseService.SubscribeWindowEnter(0, _ => events.Add(("enter", mouseService.IsInWindow())));
        mouseService.SubscribeWindowLeave(0, _ => events.Add(("leave", mouseService.IsInWindow())));
        bool isInWindowBefore = mouseService.IsInWindow();

        mouseService.OnMouseWindowPresenceEvent(true, 1);
        mouseService.OnMouseWindowPresenceEvent(true, 2);
        mouseService.OnMouseWindowPresenceEvent(false, 3);
        mouseService.OnMouseWindowPresenceEvent(false, 4);

        Assert.Multiple(() =>
        {
            Assert.That(isInWindowBefore, Is.False);
            Assert.That(events, Is.EqualTo(new[] { ("enter", true), ("leave", false) }));
            Assert.That(mouseService.IsInWindow(), Is.False);
        });
    }
}
