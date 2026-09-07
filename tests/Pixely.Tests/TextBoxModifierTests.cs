using Pixely.Input;
using Pixely.Ui;

namespace Pixely.Tests;

/// <summary>
/// That a field passes the modifiers it was given on to the editing commands. Here rather than
/// beside the other field tests because building a <see cref="Keyboard"/> with a key held needs
/// access this assembly has and the UI test project does not.
/// </summary>
public sealed class TextBoxModifierTests
{
    [Test]
    public void ShiftAndAnArrow_SelectRatherThanMove()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.KeyPressed(Scancode.Left, Held(Scancode.LeftShift));
        root.KeyPressed(Scancode.Backspace, new Keyboard());

        Assert.That(field.DisplayText, Is.EqualTo("hell"), "shift selected the last character, and backspace removed the selection");
    }

    [Test]
    public void ControlAndBackspace_RemoveAWholeWord()
    {
        TextBox field = new() { Text = "hello big world" };
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.KeyPressed(Scancode.Backspace, Held(Scancode.LeftCtrl));

        Assert.That(field.DisplayText, Is.EqualTo("hello big "));
    }

    [Test]
    public void ControlAndAnArrow_MoveAWholeWord()
    {
        TextBox field = new() { Text = "hello big world" };
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.KeyPressed(Scancode.Left, Held(Scancode.LeftCtrl));
        root.KeyPressed(Scancode.Backspace, new Keyboard());

        Assert.That(field.DisplayText, Is.EqualTo("hello bigworld"), "the caret moved a word, so the space before it went");
    }

    [Test]
    public void ControlAndC_ReachTheClipboardTheFieldWasGiven()
    {
        TextBox field = new() { Text = "hello" };
        RecordingClipboard clipboard = new();
        field.Clipboard = clipboard;
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.KeyPressed(Scancode.A, Held(Scancode.LeftCtrl));
        root.KeyPressed(Scancode.C, Held(Scancode.LeftCtrl));

        Assert.That(clipboard.Text, Is.EqualTo("hello"));
    }

    [Test]
    public void ControlAndV_PasteFromTheClipboardTheFieldWasGiven()
    {
        TextBox field = new();
        RecordingClipboard clipboard = new() { Text = "pasted" };
        field.Clipboard = clipboard;
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.KeyPressed(Scancode.V, Held(Scancode.LeftCtrl));

        Assert.That(field.DisplayText, Is.EqualTo("pasted"));
    }

    [Test]
    public void WithNoClipboardOfItsOwn_TheFieldReachesTheRootsClipboard()
    {
        TextBox field = new() { Text = "hello" };
        RecordingClipboard clipboard = new();
        UiRoot root = Rooted(field);
        root.Clipboard = clipboard;
        root.Focus(field);

        root.KeyPressed(Scancode.A, Held(Scancode.LeftCtrl));
        root.KeyPressed(Scancode.C, Held(Scancode.LeftCtrl));

        Assert.That(clipboard.Text, Is.EqualTo("hello"));
    }

    [Test]
    public void TheFieldsOwnClipboard_WinsOverTheRoots()
    {
        TextBox field = new() { Text = "hello" };
        RecordingClipboard fieldClipboard = new();
        RecordingClipboard rootClipboard = new();
        field.Clipboard = fieldClipboard;
        UiRoot root = Rooted(field);
        root.Clipboard = rootClipboard;
        root.Focus(field);

        root.KeyPressed(Scancode.A, Held(Scancode.LeftCtrl));
        root.KeyPressed(Scancode.C, Held(Scancode.LeftCtrl));

        Assert.That(fieldClipboard.Text, Is.EqualTo("hello"));
        Assert.That(rootClipboard.Text, Is.Null);
    }

    [Test]
    public void WithTheRootsDefaultClipboard_CutStillDeletesAndPasteDoesNothing()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.KeyPressed(Scancode.A, Held(Scancode.LeftCtrl));
        root.KeyPressed(Scancode.X, Held(Scancode.LeftCtrl));
        root.KeyPressed(Scancode.V, Held(Scancode.LeftCtrl));

        Assert.That(field.DisplayText, Is.Empty);
    }

    private static Keyboard Held(Scancode scancode)
    {
        Keyboard keyboard = new();
        keyboard.Set(scancode);
        return keyboard;
    }

    private static UiRoot Rooted(Element field)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { field } });
        return root;
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? Text { get; set; }

        public bool HasText => Text != null;

        public string? GetText() => Text;

        public void SetText(string text) => Text = text;
    }
}
