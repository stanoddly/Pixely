using Pixely.Gpu;
using System.Globalization;
using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// A field's behaviour: what reaches the value, when, and what happens to an edit that is abandoned
/// or refused. Nothing here lays the tree out — that is <see cref="TextLayoutTests"/>, which
/// measures against a stand-in font — and nothing here paints, which is exercised by running the
/// application.
/// </summary>
public class TextBoxTests
{
    private static readonly Keyboard NoModifiers = new();

    [Test]
    public void AnUnfocusedField_ShowsItsValue()
    {
        TextBox field = new() { Text = "hello" };

        Assert.Multiple(() =>
        {
            Assert.That(field.DisplayText, Is.EqualTo("hello"));
            Assert.That(field.IsEditing, Is.False);
        });
    }

    [Test]
    public void FocusingAField_StartsAnEditFromItsValue()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);

        root.Focus(field);

        Assert.Multiple(() =>
        {
            Assert.That(field.IsEditing, Is.True);
            Assert.That(field.DisplayText, Is.EqualTo("hello"));
        });
    }

    [Test]
    public void TypingIntoAFocusedField_LeavesTheValueAlone()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.TextEntered("!");

        Assert.Multiple(() =>
        {
            Assert.That(field.DisplayText, Is.EqualTo("hello!"));
            Assert.That(field.Text, Is.EqualTo("hello"), "nothing reaches the value until the edit finishes");
        });
    }

    [Test]
    public void AssigningWhileFocused_DoesNotDisturbWhatIsBeingTyped()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        root.TextEntered("!");

        field.Text = "elsewhere";

        Assert.That(field.DisplayText, Is.EqualTo("hello!"),
            "a value changing under someone's hands mid-word is worse than it arriving late");
    }

    [Test]
    public void Enter_CommitsAndGivesUpFocus()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        root.TextEntered("!");
        List<string> committed = new();
        field.Committed += committed.Add;

        root.KeyPressed(Scancode.Return, NoModifiers);

        Assert.Multiple(() =>
        {
            Assert.That(field.Text, Is.EqualTo("hello!"));
            Assert.That(committed, Is.EqualTo(new[] { "hello!" }));
            Assert.That(root.FocusedElement, Is.Null);
        });
    }

    [Test]
    public void Escape_DiscardsAndGivesUpFocus()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        root.TextEntered("!");
        List<string> committed = new();
        field.Committed += committed.Add;

        root.KeyPressed(Scancode.Escape, NoModifiers);

        Assert.Multiple(() =>
        {
            Assert.That(field.Text, Is.EqualTo("hello"));
            Assert.That(committed, Is.Empty);
            Assert.That(root.FocusedElement, Is.Null);
        });
    }

    [Test]
    public void LosingFocus_Commits()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        root.TextEntered("!");

        root.Focus(null);

        Assert.That(field.Text, Is.EqualTo("hello!"));
    }

    [Test]
    public void TheEditIsOverBeforeCommittedIsRaised()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        bool editingDuringCommit = true;
        field.Committed += _ => editingDuringCommit = field.IsEditing;

        root.KeyPressed(Scancode.Return, NoModifiers);

        Assert.That(editingDuringCommit, Is.False,
            "a handler that assigns to this field is not fighting an edit that is still in progress");
    }

    [Test]
    public void AHandlerThatAssignsBackToTheField_Sticks()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        root.TextEntered("!");
        field.Committed += _ => field.Text = "rewritten";

        root.KeyPressed(Scancode.Return, NoModifiers);

        Assert.That(field.Text, Is.EqualTo("rewritten"));
    }

    [Test]
    public void AKeyThatMeansNothingToTheField_IsLeftForTheGame()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);

        Assert.That(root.KeyPressed(Scancode.F1, NoModifiers), Is.False);
    }

    [Test]
    public void AnUnfocusedField_TakesNoKeys()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);

        Assert.Multiple(() =>
        {
            Assert.That(root.KeyPressed(Scancode.Backspace, NoModifiers), Is.False);
            Assert.That(root.TextEntered("x"), Is.False);
            Assert.That(field.Text, Is.EqualTo("hello"));
        });
    }

    [Test]
    public void ANumberField_RefusesLettersWhileTyping()
    {
        NumberBox<float> field = new(formatProvider: CultureInfo.InvariantCulture);
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.TextEntered("1");
        root.TextEntered("x");
        root.TextEntered("2");

        Assert.That(field.DisplayText, Is.EqualTo("12"));
    }

    [TestCase("-")]
    [TestCase("1.")]
    [TestCase("-1.")]
    public void ANumberField_AcceptsWhatIsOnTheWayToANumber(string typed)
    {
        NumberBox<float> field = new(formatProvider: CultureInfo.InvariantCulture);
        UiRoot root = Rooted(field);
        root.Focus(field);

        foreach (char character in typed)
        {
            root.TextEntered(character.ToString());
        }

        Assert.That(field.DisplayText, Is.EqualTo(typed), "refusing these would make the number they lead to unreachable");
    }

    [Test]
    public void ANumberField_WillNotCommitSomethingIncomplete()
    {
        NumberBox<float> field = new(formatProvider: CultureInfo.InvariantCulture) { Text = "5" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        root.KeyPressed(Scancode.Backspace, NoModifiers);
        root.TextEntered("-");

        root.KeyPressed(Scancode.Return, NoModifiers);

        Assert.Multiple(() =>
        {
            Assert.That(field.DisplayText, Is.EqualTo("-"), "what was typed is still there to be fixed");
            Assert.That(field.Text, Is.EqualTo("5"), "the old value is still what the application sees");
            Assert.That(root.FocusedElement, Is.SameAs(field), "and the caret stays where the problem is");
        });
    }

    [Test]
    public void ANumberFieldLosingFocusMidEdit_ThrowsTheEditAway()
    {
        NumberBox<float> field = new(formatProvider: CultureInfo.InvariantCulture) { Text = "5" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        root.TextEntered("-");

        root.Focus(null);

        Assert.That(field.Text, Is.EqualTo("5"));
    }

    [Test]
    public void ANumberField_ReportsItsValueAndHonoursItsCulture()
    {
        NumberBox<float> field = new(formatProvider: new CultureInfo("de-DE"));
        UiRoot root = Rooted(field);
        root.Focus(field);
        List<float> committed = new();
        field.ValueCommitted += committed.Add;

        foreach (char character in "1,5")
        {
            root.TextEntered(character.ToString());
        }

        root.KeyPressed(Scancode.Return, NoModifiers);

        Assert.Multiple(() =>
        {
            Assert.That(committed, Is.EqualTo(new[] { 1.5f }));
            Assert.That(field.Value, Is.EqualTo(1.5f));
        });
    }

    [Test]
    public void ANumberField_RefusesInfinity()
    {
        NumberBox<float> field = new(formatProvider: CultureInfo.InvariantCulture) { Text = "1" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        root.KeyPressed(Scancode.Backspace, NoModifiers);
        root.TextEntered("Infinity");

        root.KeyPressed(Scancode.Return, NoModifiers);

        Assert.That(field.Text, Is.EqualTo("1"), "parseable, but not a value a field like this is asking for");
    }

    [Test]
    public void SettingANumberFieldsValue_WritesItInItsOwnCulture()
    {
        NumberBox<float> field = new(formatProvider: new CultureInfo("de-DE"));

        field.SetValue(1.5f);

        Assert.That(field.Text, Is.EqualTo("1,5"));
    }

    [Test]
    public void MovingBetweenTwoFields_CommitsTheFirstBeforeTheSecondTakesOver()
    {
        TextBox first = new() { Text = "one" };
        TextBox second = new() { Text = "two" };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { first, second } });
        root.Focus(first);
        root.TextEntered("!");

        List<string> order = new();
        first.Committed += value => order.Add($"committed {value}");
        second.Committed += _ => order.Add("second committed");
        root.FocusChanged += focused => order.Add(focused == second ? "second focused" : "focus moved");

        root.Focus(second);

        Assert.That(order, Is.EqualTo(new[] { "committed one!", "second focused" }),
            "the outgoing field has written its value before anything the incoming one does can read it");
    }

    [Test]
    public void ACommitHandlerThatFocusesAnotherField_KeepsThatFocus()
    {
        TextBox first = new() { Text = "one" };
        TextBox second = new() { Text = "two" };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { first, second } });
        root.Focus(first);
        first.Committed += _ => root.Focus(second);

        root.KeyPressed(Scancode.Return, NoModifiers);

        Assert.That(root.FocusedElement, Is.SameAs(second),
            "moving to the next field on Enter is the ordinary thing to want, and giving up focus afterwards would undo it");
    }

    [Test]
    public void ACommitHandlerThatThrows_StillLeavesTheFieldUsable()
    {
        TextBox field = new() { Text = "one" };
        UiRoot root = Rooted(field);
        root.Focus(field);
        field.Committed += _ => throw new InvalidOperationException("save failed");

        Assert.Throws<InvalidOperationException>(() => root.KeyPressed(Scancode.Return, NoModifiers));

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.Null, "focus was given up before the handler ran");
            Assert.That(field.IsEditing, Is.False, "so the field is not left refusing every key it is sent");
        });
    }

    [Test]
    public void WithNoClipboard_CopyAndPasteDoNothing()
    {
        TextBox field = new() { Text = "hello" };
        UiRoot root = Rooted(field);
        root.Focus(field);

        Assert.That(field.Clipboard, Is.Null, "a field is given one, rather than finding one of its own");
    }

    [Test]
    public void BlurringAFieldNobodyChanged_StillRepaintsIt()
    {
        FontlessTextBox field = new() { Text = "hello" };
        UiRoot root = new();
        root.AddLayer(new Column { Children = { field } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        root.Focus(field);
        root.Update();

        root.Focus(null);

        Assert.That(field.IsPaintDirty, Is.True,
            "the caret was on screen a moment ago, and nothing else is going to ask for it to be taken off");
    }

    [Test]
    public void PressingAField_FocusesIt()
    {
        FontlessTextBox field = new();
        UiRoot root = new();
        root.AddLayer(new Column { Children = { field } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        root.PointerPressed(new Vector2Int(10, 5), MouseButton.Left);

        Assert.That(root.FocusedElement, Is.SameAs(field));
    }

    [Test]
    public void ARightPressOnAField_IsLeftForTheGame()
    {
        FontlessTextBox field = new();
        UiRoot root = new();
        root.AddLayer(new Column { Children = { field } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        Assert.That(root.PointerPressed(new Vector2Int(10, 5), MouseButton.Right), Is.False);
    }

    [Test]
    public void AFocusedField_LooksDifferentFromAnIdleOne()
    {
        TextBox field = new();
        UiRoot root = Rooted(field);

        VisualState idle = field.VisualState;
        root.Focus(field);

        Assert.Multiple(() =>
        {
            Assert.That(idle, Is.EqualTo(VisualState.Normal));
            Assert.That(field.VisualState, Is.EqualTo(VisualState.Focused), "a field shows which one the typing goes to");
        });
    }

    [Test]
    public void AFieldDisabledWhileBeingTypedInto_ReadsAsDisabledRatherThanFocused()
    {
        TextBox field = new();
        UiRoot root = Rooted(field);
        root.Focus(field);

        // Disabled while the edit is still open, so both states are true at once and only the order
        // they are checked in decides the answer.
        field.IsEnabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(field.IsEditing, Is.True);
            Assert.That(field.VisualState, Is.EqualTo(VisualState.Disabled));
        });
    }

    [Test]
    public void ADisabledField_CannotBeTypedIntoAtAll()
    {
        TextBox field = new() { IsEnabled = false };
        UiRoot root = Rooted(field);

        root.Focus(field);

        Assert.That(root.FocusedElement, Is.Null);
    }

    [Test]
    public void BackgroundsAssignedToTheField_BeatTheStyleAndFallBackWithin()
    {
        StateDrawables backgrounds = new(new SolidDrawable(Colors.Red)) { Focused = new SolidDrawable(Colors.Green) };
        ExposedTextBox focused = new() { Backgrounds = backgrounds };
        ExposedTextBox plain = new() { Backgrounds = new StateDrawables(backgrounds.Normal) };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { focused, plain } });
        root.Focus(focused);

        Assert.Multiple(() =>
        {
            Assert.That(focused.ResolvedBackground(), Is.SameAs(backgrounds.Focused));
            Assert.That(plain.ResolvedBackground(), Is.SameAs(backgrounds.Normal), "an unfocused field takes the ordinary one");
        });
    }

    [Test]
    public void AFieldsOwnBackgrounds_BeatAStyleThatAlsoSuppliesThem()
    {
        StateDrawables mine = new(new SolidDrawable(Colors.Red));
        ExposedTextBox field = new() { Backgrounds = mine };
        UiRoot root = new() { Style = new UiStyle { FieldBackground = new StateDrawables(new SolidDrawable(Colors.Blue)) } };
        root.AddLayer(new Column { Children = { field } });

        Assert.That(field.ResolvedBackground(), Is.SameAs(mine.Normal));
    }

    [Test]
    public void APlainBackgroundAssignedToTheField_BeatsTheStyle()
    {
        SolidDrawable plain = new(Colors.Red);
        ExposedTextBox field = new() { Background = plain };
        UiRoot root = new() { Style = new UiStyle { FieldBackground = new StateDrawables(new SolidDrawable(Colors.Blue)) } };
        root.AddLayer(new Column { Children = { field } });
        root.Focus(field);

        Assert.That(field.ResolvedBackground(), Is.SameAs(plain),
            "one look for every state is what assigning it plainly asks for");
    }

    [Test]
    public void WithNoBackgroundsOfItsOwn_AFieldTakesTheStylesAndThenTheDefault()
    {
        StateDrawables styled = new(new SolidDrawable(Colors.Blue));
        ExposedTextBox fromStyle = new();
        ExposedTextBox fromDefault = new();
        UiRoot styledRoot = new() { Style = new UiStyle { FieldBackground = styled } };
        styledRoot.AddLayer(new Column { Children = { fromStyle } });
        UiRoot plainRoot = new();
        plainRoot.AddLayer(new Column { Children = { fromDefault } });

        Assert.Multiple(() =>
        {
            Assert.That(fromStyle.ResolvedBackground(), Is.SameAs(styled.Normal));
            Assert.That(fromDefault.ResolvedBackground(), Is.SameAs(TextBox.DefaultBackground.Normal));
        });
    }

    /// <summary>Opens the resolved background up, which is protected because only painting reads it.</summary>
    private sealed class ExposedTextBox : TextBox
    {
        public Drawable? ResolvedBackground() => EffectiveBackground;
    }

    /// <summary>
    /// A field that can be laid out without a font, so a test can run a real pass over one. Measuring
    /// and drawing text needs a device to rasterise with, and everything below those two methods is
    /// the same field.
    /// </summary>
    private sealed class FontlessTextBox : TextBox
    {
        protected override Vector2Int MeasureContent(Constraints constraints) => new(60, 12);

        protected override void PaintContent(PaintContext context)
        {
        }
    }

    /// <summary>
    /// Rooted but never laid out: <see cref="UiRoot.Update"/> would measure, and measuring text needs
    /// a font. Focus only needs the element to be reachable, which being in the tree is enough for.
    /// </summary>
    private static UiRoot Rooted(Element field)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { field } });
        return root;
    }
}
