using System.Numerics;
using Pixely.Input;
using SDL;

namespace Pixely.Tests;

public sealed class MouseWheelEventTests
{
    [Test]
    public void Wheel_NormalDirectionIsReportedAsSent()
    {
        MouseService mouseService = new(new WindowRegistry());
        Vector2 delta = default;
        mouseService.SubscribeWheel(0, args => delta = args.Delta);

        mouseService.OnMouseWheelEvent(default, new SDL_MouseWheelEvent
        {
            x = 1,
            y = -2,
            direction = SDL_MouseWheelDirection.SDL_MOUSEWHEEL_NORMAL
        });

        Assert.That(delta, Is.EqualTo(new Vector2(1, -2)));
    }

    [Test]
    public void Wheel_FlippedDirectionIsUndoneForNaturalScrolling()
    {
        MouseService mouseService = new(new WindowRegistry());
        Vector2 delta = default;
        mouseService.SubscribeWheel(0, args => delta = args.Delta);

        mouseService.OnMouseWheelEvent(default, new SDL_MouseWheelEvent
        {
            x = 1,
            y = -2,
            direction = SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED
        });

        Assert.That(delta, Is.EqualTo(new Vector2(-1, 2)));
    }
}
