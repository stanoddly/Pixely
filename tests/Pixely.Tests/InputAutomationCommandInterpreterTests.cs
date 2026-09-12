using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tests;

public sealed class InputAutomationCommandInterpreterTests
{
    [TestCase("mouse move 320 180", "MouseMoveTo <320, 180>")]
    [TestCase("mouse moveby 10 -5.5", "MouseMoveBy <10, -5.5>")]
    [TestCase("mouse down left 1 2", "MouseDown Left <1, 2>")]
    [TestCase("mouse up Right 1 2", "MouseUp Right <1, 2>")]
    [TestCase("  mouse  click  Left  330 175", "MouseClick Left <330, 175>")]
    [TestCase("mouse wheel 0 -1 330 175", "MouseWheel <0, -1> <330, 175>")]
    [TestCase("key down LeftCtrl", "KeyDown LeftCtrl")]
    [TestCase("key up e", "KeyUp E")]
    [TestCase("key press Return", "KeyPress Return")]
    [TestCase("text hello  world ", "TextInput 'hello  world '")]
    [TestCase("text", "TextInput ''")]
    public void Execute_DispatchesCommand(string line, string expectedCall)
    {
        RecordingAutomation automation = new();
        List<string> replies = new();
        InputAutomationCommandInterpreter interpreter = new(automation, null, new RecordingImageWriter(), replies.Add);

        interpreter.Execute(line);

        Assert.Multiple(() =>
        {
            Assert.That(replies, Is.EqualTo(new[] { "ok" }));
            Assert.That(automation.Calls, Is.EqualTo(new[] { expectedCall }));
        });
    }

    [TestCase("", null)]
    [TestCase("   ", null)]
    [TestCase("# a comment", null)]
    [TestCase("jump", "error: unknown command 'jump'")]
    [TestCase("mouse move 320", "error: unknown command 'mouse move 320'")]
    [TestCase("mouse move x 180", "error: invalid number 'x'")]
    [TestCase("mouse click Center 1 2", "error: unknown MouseButton 'Center'")]
    [TestCase("mouse click 9 1 2", "error: unknown MouseButton '9'")]
    [TestCase("key press Ctrl", "error: unknown Scancode 'Ctrl'")]
    [TestCase("screenshot", "error: unknown command 'screenshot'")]
    public void Execute_RejectsMalformedLineWithoutDispatching(string line, string? expectedReply)
    {
        RecordingAutomation automation = new();
        List<string> replies = new();
        InputAutomationCommandInterpreter interpreter = new(automation, null, new RecordingImageWriter(), replies.Add);

        interpreter.Execute(line);

        Assert.Multiple(() =>
        {
            Assert.That(replies, Is.EqualTo(expectedReply is null ? Array.Empty<string>() : new[] { expectedReply }));
            Assert.That(automation.Calls, Is.Empty);
        });
    }

    [Test]
    public void Execute_PropagatesAutomationFailure()
    {
        InputAutomationCommandInterpreter interpreter = new(new ThrowingAutomation(), null, new RecordingImageWriter(), _ => { });

        Assert.Throws<InvalidOperationException>(() => interpreter.Execute("key press A"));
    }

    [Test]
    public void Execute_Screenshot_RepliesAfterTheFrameIsWritten()
    {
        FakeFrameCapture frameCapture = new();
        RecordingImageWriter imageWriter = new();
        List<string> replies = new();
        InputAutomationCommandInterpreter interpreter = new(new RecordingAutomation(), frameCapture, imageWriter, replies.Add);

        interpreter.Execute("screenshot  /tmp/my frames/frame.png ");
        Assert.That(replies, Is.Empty);

        RawImage image = new(new byte[4], new ShortSize(1, 1), PixelFormat.Abgr8888);
        frameCapture.Complete(image);

        Assert.Multiple(() =>
        {
            Assert.That(imageWriter.Saved, Is.EqualTo(new[] { (image, "/tmp/my frames/frame.png") }));
            Assert.That(replies, Is.EqualTo(new[] { "ok" }));
        });
    }

    [Test]
    public void Execute_Screenshot_ReportsWriteFailure()
    {
        FakeFrameCapture frameCapture = new();
        List<string> replies = new();
        InputAutomationCommandInterpreter interpreter = new(new RecordingAutomation(), frameCapture, new ThrowingImageWriter(), replies.Add);

        interpreter.Execute("screenshot /nope/frame.png");
        frameCapture.Complete(new RawImage(new byte[4], new ShortSize(1, 1), PixelFormat.Abgr8888));

        Assert.That(replies, Is.EqualTo(new[] { "error: disk full" }));
    }

    [Test]
    public void Execute_Screenshot_WithoutFrameCapture_Fails()
    {
        List<string> replies = new();
        InputAutomationCommandInterpreter interpreter = new(new RecordingAutomation(), null, new RecordingImageWriter(), replies.Add);

        interpreter.Execute("screenshot frame.png");

        Assert.That(replies, Is.EqualTo(new[] { "error: screenshots need offscreen rendering, register it with UseOffscreenRendering()" }));
    }

    private sealed class FakeFrameCapture : IFrameCapture
    {
        private readonly List<Action<Image>> _requests = new();

        public void CaptureNextFrame(Action<Image> onCaptured) => _requests.Add(onCaptured);

        public void Complete(Image image)
        {
            foreach (Action<Image> request in _requests)
            {
                request(image);
            }
            _requests.Clear();
        }
    }

    private sealed class RecordingImageWriter : IImageWriter
    {
        public List<(Image Image, string Path)> Saved { get; } = new();

        public void SavePng(Image image, string path) => Saved.Add((image, path));
    }

    private sealed class ThrowingImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path) => throw new IOException("disk full");
    }

    private sealed class RecordingAutomation : IInputAutomation
    {
        public List<string> Calls { get; } = new();

        public void MouseMoveTo(Vector2 windowPosition, ViewScope viewScope = default) => Calls.Add($"MouseMoveTo {windowPosition}");
        public void MouseMoveBy(Vector2 delta, ViewScope viewScope = default) => Calls.Add($"MouseMoveBy {delta}");
        public void MouseDown(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => Calls.Add($"MouseDown {button} {windowPosition}");
        public void MouseUp(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => Calls.Add($"MouseUp {button} {windowPosition}");
        public void MouseClick(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => Calls.Add($"MouseClick {button} {windowPosition}");
        public void MouseWheel(Vector2 delta, Vector2 windowPosition, ViewScope viewScope = default) => Calls.Add($"MouseWheel {delta} {windowPosition}");
        public void KeyDown(Scancode scancode, ViewScope viewScope = default) => Calls.Add($"KeyDown {scancode}");
        public void KeyUp(Scancode scancode, ViewScope viewScope = default) => Calls.Add($"KeyUp {scancode}");
        public void KeyPress(Scancode scancode, ViewScope viewScope = default) => Calls.Add($"KeyPress {scancode}");
        public void TextInput(string text, ViewScope viewScope = default) => Calls.Add($"TextInput '{text}'");
    }

    private sealed class ThrowingAutomation : IInputAutomation
    {
        private static InvalidOperationException Failure => new("ViewScope 0 is not registered");

        public void MouseMoveTo(Vector2 windowPosition, ViewScope viewScope = default) => throw Failure;
        public void MouseMoveBy(Vector2 delta, ViewScope viewScope = default) => throw Failure;
        public void MouseDown(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => throw Failure;
        public void MouseUp(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => throw Failure;
        public void MouseClick(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => throw Failure;
        public void MouseWheel(Vector2 delta, Vector2 windowPosition, ViewScope viewScope = default) => throw Failure;
        public void KeyDown(Scancode scancode, ViewScope viewScope = default) => throw Failure;
        public void KeyUp(Scancode scancode, ViewScope viewScope = default) => throw Failure;
        public void KeyPress(Scancode scancode, ViewScope viewScope = default) => throw Failure;
        public void TextInput(string text, ViewScope viewScope = default) => throw Failure;
    }
}
