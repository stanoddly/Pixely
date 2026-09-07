using System.Numerics;
using Pixely.Input;
using SDL;

namespace Pixely.Tests;

public class MouseStateTests
{
    [TestCase(MouseButton.Left)]
    [TestCase(MouseButton.Middle)]
    [TestCase(MouseButton.Right)]
    [TestCase(MouseButton.X1)]
    [TestCase(MouseButton.X2)]
    public void IsPressedReturnsTrueForSetButton(MouseButton button)
    {
        int buttonFlags = 1 << ((int)button - 1);
        MouseState state = new MouseState(Vector2.Zero, buttonFlags);

        Assert.That(state.IsPressed(button), Is.True);
    }

    [Test]
    public void MouseStateTracksEventDerivedMouseState()
    {
        Mouse mouse = new Mouse((SDL_MouseID)1);
        Vector2 position = new Vector2(10.5f, 20.25f);

        mouse.Position = position;
        mouse.Set(MouseButton.Middle);

        Assert.Multiple(() =>
        {
            Assert.That(mouse.State.Position, Is.EqualTo(position));
            Assert.That(mouse.State.IsPressed(MouseButton.Middle), Is.True);
            Assert.That(mouse.Position, Is.EqualTo(position));
            Assert.That(mouse.IsPressed(MouseButton.Middle), Is.True);
        });
    }

    [Test]
    public void UnsetReportsWhetherTheButtonWasPressed()
    {
        Mouse mouse = new Mouse((SDL_MouseID)1);

        mouse.Set(MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(mouse.Unset(MouseButton.Left), Is.True);
            Assert.That(mouse.Unset(MouseButton.Left), Is.False);
            Assert.That(mouse.Unset(MouseButton.Right), Is.False);
        });
    }
}
