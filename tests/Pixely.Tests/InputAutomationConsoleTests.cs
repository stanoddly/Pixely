using System.Numerics;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationConsoleTests
{
    [Test]
    public void Update_RunsQueuedLinesOnCallingThreadAndRepliesInOrder()
    {
        List<(string Call, int ThreadId)> calls = new();
        StringWriter output = new();
        InputAutomationConsole console = new(new RecordingAutomation(calls), new StringReader("key press A\n\nbogus\nkey press B\n"), output);

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (calls.Count < 2 && DateTime.UtcNow < deadline)
        {
            console.Update();
            Thread.Yield();
        }

        Assert.Multiple(() =>
        {
            Assert.That(calls.Select(call => call.Call), Is.EqualTo(new[] { "KeyPress A", "KeyPress B" }));
            Assert.That(calls.Select(call => call.ThreadId), Is.All.EqualTo(Environment.CurrentManagedThreadId));
            Assert.That(output.ToString(), Is.EqualTo($"ok{Environment.NewLine}error: unknown command 'bogus'{Environment.NewLine}ok{Environment.NewLine}"));
        });
    }

    [Test]
    public void Update_WithEmptyInput_DoesNothing()
    {
        StringWriter output = new();
        InputAutomationConsole console = new(new RecordingAutomation(new()), new StringReader(""), output);

        console.Update();
        console.Update();

        Assert.That(output.ToString(), Is.Empty);
    }

    private sealed class RecordingAutomation(List<(string Call, int ThreadId)> calls) : IInputAutomation
    {
        private void Record(string call) => calls.Add((call, Environment.CurrentManagedThreadId));

        public void MouseMoveTo(Vector2 windowPosition, ViewScope viewScope = default) => Record($"MouseMoveTo {windowPosition}");
        public void MouseMoveBy(Vector2 delta, ViewScope viewScope = default) => Record($"MouseMoveBy {delta}");
        public void MouseDown(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => Record($"MouseDown {button} {windowPosition}");
        public void MouseUp(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => Record($"MouseUp {button} {windowPosition}");
        public void MouseClick(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default) => Record($"MouseClick {button} {windowPosition}");
        public void MouseWheel(Vector2 delta, Vector2 windowPosition, ViewScope viewScope = default) => Record($"MouseWheel {delta} {windowPosition}");
        public void KeyDown(Scancode scancode, ViewScope viewScope = default) => Record($"KeyDown {scancode}");
        public void KeyUp(Scancode scancode, ViewScope viewScope = default) => Record($"KeyUp {scancode}");
        public void KeyPress(Scancode scancode, ViewScope viewScope = default) => Record($"KeyPress {scancode}");
        public void TextInput(string text, ViewScope viewScope = default) => Record($"TextInput '{text}'");
    }
}
