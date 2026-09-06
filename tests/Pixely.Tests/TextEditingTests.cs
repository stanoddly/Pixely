using Pixely.Input;
using Pixely.Text;

namespace Pixely.Tests;

/// <summary>
/// The editing model both the immediate-mode and retained-mode fields are built on. It had no tests
/// of its own while it lived inside Pencil, so these are what say the two share the same behaviour
/// rather than merely the same code.
/// </summary>
public sealed class TextEditingTests
{
    [Test]
    public void ANewBuffer_PutsTheCaretAtTheEnd()
    {
        TextEditingBuffer buffer = new("hello");

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Text, Is.EqualTo("hello"));
            Assert.That(buffer.CursorPosition, Is.EqualTo(5));
            Assert.That(buffer.HasSelection, Is.False);
        });
    }

    [Test]
    public void InsertingText_ReplacesTheSelection()
    {
        TextEditingBuffer buffer = Selected("hello world", 6, 11);

        buffer.TryInsertText("there");

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Text, Is.EqualTo("hello there"));
            Assert.That(buffer.CursorPosition, Is.EqualTo(11));
            Assert.That(buffer.HasSelection, Is.False);
        });
    }

    [Test]
    public void AnEditTheFilterRefuses_ChangesNothing()
    {
        TextEditingBuffer buffer = new("12", candidate => candidate.All(char.IsDigit));

        bool inserted = buffer.TryInsertText("a");

        Assert.Multiple(() =>
        {
            Assert.That(inserted, Is.False);
            Assert.That(buffer.Text, Is.EqualTo("12"));
            Assert.That(buffer.CursorPosition, Is.EqualTo(2));
        });
    }

    [Test]
    public void ResettingTheText_IgnoresTheFilter()
    {
        TextEditingBuffer buffer = new("12", candidate => candidate.All(char.IsDigit));

        // The value came from the application, which is not restricted to what a user could type.
        buffer.Reset("n/a");

        Assert.That(buffer.Text, Is.EqualTo("n/a"));
    }

    [Test]
    public void Backspace_DeletesTheSelectionRatherThanOneCharacter()
    {
        TextEditingBuffer buffer = Selected("hello", 1, 4);

        Handle(buffer, Scancode.Backspace);

        Assert.That(buffer.Text, Is.EqualTo("ho"));
    }

    [Test]
    public void BackspaceWithControl_DeletesAWord()
    {
        TextEditingBuffer buffer = new("hello world");

        Handle(buffer, Scancode.Backspace, ctrl: true);

        Assert.That(buffer.Text, Is.EqualTo("hello "));
    }

    [Test]
    public void DeleteWithControl_DeletesTheWordAhead()
    {
        TextEditingBuffer buffer = new("hello world") { CursorPosition = 0 };

        Handle(buffer, Scancode.Delete, ctrl: true);

        Assert.That(buffer.Text, Is.EqualTo("world"));
    }

    [Test]
    public void BackspaceAtTheStart_DoesNothing()
    {
        TextEditingBuffer buffer = new("hi") { CursorPosition = 0 };

        Handle(buffer, Scancode.Backspace);

        Assert.That(buffer.Text, Is.EqualTo("hi"));
    }

    [Test]
    public void AnArrowKeyWithASelection_CollapsesToTheEdgeItMovesTowards()
    {
        TextEditingBuffer buffer = Selected("hello", 1, 4);

        Handle(buffer, Scancode.Left);

        Assert.Multiple(() =>
        {
            Assert.That(buffer.CursorPosition, Is.EqualTo(1), "not one left of where the caret happened to be");
            Assert.That(buffer.HasSelection, Is.False);
        });
    }

    [Test]
    public void AnArrowKeyWithShift_ExtendsTheSelection()
    {
        TextEditingBuffer buffer = new("hello");

        Handle(buffer, Scancode.Left, shift: true);
        Handle(buffer, Scancode.Left, shift: true);

        Assert.Multiple(() =>
        {
            Assert.That(buffer.GetSelectedText(), Is.EqualTo("lo"));
            Assert.That(buffer.CursorPosition, Is.EqualTo(3));
        });
    }

    [Test]
    public void HomeAndEnd_MoveTheCaretToEitherEnd()
    {
        TextEditingBuffer buffer = new("hello");

        Handle(buffer, Scancode.Home);
        Assert.That(buffer.CursorPosition, Is.Zero);

        Handle(buffer, Scancode.End);
        Assert.That(buffer.CursorPosition, Is.EqualTo(5));
    }

    [Test]
    public void HomeWithShift_SelectsBackToTheStart()
    {
        TextEditingBuffer buffer = new("hello");

        Handle(buffer, Scancode.Home, shift: true);

        Assert.That(buffer.GetSelectedText(), Is.EqualTo("hello"));
    }

    [Test]
    public void ControlA_SelectsEverything()
    {
        TextEditingBuffer buffer = new("hello") { CursorPosition = 2 };

        Handle(buffer, Scancode.A, ctrl: true);

        Assert.That(buffer.GetSelectedText(), Is.EqualTo("hello"));
    }

    [Test]
    public void AWithoutControl_IsNotAnEditingKey()
    {
        TextEditingBuffer buffer = new("hello");

        Assert.That(Handle(buffer, Scancode.A), Is.EqualTo(TextEditingOutcome.Ignored),
            "an ordinary letter is text input, and arrives that way instead");
    }

    [Test]
    public void ControlC_CopiesTheSelectionAndLeavesItAlone()
    {
        TextEditingBuffer buffer = Selected("hello", 0, 2);
        RecordingClipboard clipboard = new();

        Handle(buffer, Scancode.C, ctrl: true, clipboard: clipboard);

        Assert.Multiple(() =>
        {
            Assert.That(clipboard.Text, Is.EqualTo("he"));
            Assert.That(buffer.Text, Is.EqualTo("hello"));
        });
    }

    [Test]
    public void ControlX_CutsTheSelection()
    {
        TextEditingBuffer buffer = Selected("hello", 0, 2);
        RecordingClipboard clipboard = new();

        Handle(buffer, Scancode.X, ctrl: true, clipboard: clipboard);

        Assert.Multiple(() =>
        {
            Assert.That(clipboard.Text, Is.EqualTo("he"));
            Assert.That(buffer.Text, Is.EqualTo("llo"));
        });
    }

    [Test]
    public void ACutTheFilterRefuses_LeavesTheClipboardAlone()
    {
        TextEditingBuffer buffer = new("123", candidate => candidate.Length == 3) { SelectionAnchor = 0, CursorPosition = 2 };
        RecordingClipboard clipboard = new() { Text = "previous" };

        Handle(buffer, Scancode.X, ctrl: true, clipboard: clipboard);

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Text, Is.EqualTo("123"));
            Assert.That(clipboard.Text, Is.EqualTo("previous"), "nothing was cut, so nothing replaced what was there");
        });
    }

    [Test]
    public void ControlV_PastesAtTheCaret()
    {
        TextEditingBuffer buffer = new("ho") { CursorPosition = 1 };
        RecordingClipboard clipboard = new() { Text = "ell" };

        Handle(buffer, Scancode.V, ctrl: true, clipboard: clipboard);

        Assert.That(buffer.Text, Is.EqualTo("hello"));
    }

    [Test]
    public void ControlVWithAnEmptyClipboard_ChangesNothing()
    {
        TextEditingBuffer buffer = new("ho");

        Handle(buffer, Scancode.V, ctrl: true, clipboard: new RecordingClipboard { Text = null });

        Assert.That(buffer.Text, Is.EqualTo("ho"));
    }

    [TestCase(Scancode.Return)]
    [TestCase(Scancode.Return2)]
    [TestCase(Scancode.KeypadEnter)]
    public void Enter_AsksToCommit(Scancode scancode)
    {
        Assert.That(Handle(new TextEditingBuffer("x"), scancode), Is.EqualTo(TextEditingOutcome.Commit));
    }

    [Test]
    public void Escape_AsksToCancel()
    {
        Assert.That(Handle(new TextEditingBuffer("x"), Scancode.Escape), Is.EqualTo(TextEditingOutcome.Cancel));
    }

    [Test]
    public void AKeyThatMeansNothingHere_IsLeftAlone()
    {
        Assert.That(Handle(new TextEditingBuffer("x"), Scancode.F1), Is.EqualTo(TextEditingOutcome.Ignored));
    }

    private static TextEditingOutcome Handle(
        TextEditingBuffer buffer,
        Scancode scancode,
        bool shift = false,
        bool ctrl = false,
        IClipboardService? clipboard = null) =>
        TextEditingCommands.HandleKey(buffer, scancode, shift, ctrl, clipboard);

    private static TextEditingBuffer Selected(string text, int anchor, int caret) =>
        new(text) { SelectionAnchor = anchor, CursorPosition = caret };

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? Text { get; set; }

        public bool HasText => Text != null;

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }
}
