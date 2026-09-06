using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// The bridge between the platform's input services and the root: that keys and committed text
/// reach it at all, that an unused one is left for the game, and when the platform is asked to
/// start and stop text input.
/// </summary>
public class UiInputSystemTests
{
    [Test]
    public void AKeyPress_ReachesTheFocusedElementAndIsConsumed()
    {
        FakeKeyboardService keyboard = new();
        FakeTextInputService textInput = new();
        RecordingFocusTarget field = Sized();
        UiRoot root = Bridged(textInput, keyboard, new Column { Children = { field } }, new ViewScope(3), inputOrder: -7);
        root.Focus(field);
        field.Calls.Clear();

        KeyEventArgs keyEvent = new();
        keyboard.KeyDownHandler!(keyEvent);

        Assert.Multiple(() =>
        {
            Assert.That(field.Calls, Has.Count.EqualTo(1), "the key reached the field");
            Assert.That(keyEvent.Consumed, Is.True);
            Assert.That(keyboard.KeyDownSubscription, Is.EqualTo((new ViewScope(3), -7)), "subscribed for its own window, at the order it was given");
        });
    }

    [Test]
    public void AKeyTheFieldDoesNotWant_IsLeftForTheGame()
    {
        FakeKeyboardService keyboard = new();
        FakeTextInputService textInput = new();
        RecordingFocusTarget field = Sized();
        UiRoot root = Bridged(textInput, keyboard, new Column { Children = { field } });
        root.Focus(field);
        field.HandlesKeys = false;

        KeyEventArgs keyEvent = new();
        keyboard.KeyDownHandler!(keyEvent);

        Assert.That(keyEvent.Consumed, Is.False);
    }

    [Test]
    public void CommittedText_ReachesTheFocusedElementAndIsConsumed()
    {
        FakeKeyboardService keyboard = new();
        FakeTextInputService textInput = new();
        RecordingFocusTarget field = Sized();
        UiRoot root = Bridged(textInput, keyboard, new Column { Children = { field } }, new ViewScope(3), inputOrder: -7);
        root.Focus(field);
        field.Calls.Clear();

        TextInputEventArgs textEvent = new();
        textInput.TextInputHandler!(textEvent);

        Assert.Multiple(() =>
        {
            Assert.That(field.Calls, Is.EqualTo(new[] { "text " }));
            Assert.That(textEvent.Consumed, Is.True);
            Assert.That(textInput.TextInputSubscription, Is.EqualTo((new ViewScope(3), -7)));
        });
    }

    [Test]
    public void TextWithNothingFocused_IsLeftForTheGame()
    {
        FakeKeyboardService keyboard = new();
        FakeTextInputService textInput = new();
        UiRoot root = Bridged(textInput, keyboard, new Column { Children = { Sized() } });

        TextInputEventArgs textEvent = new();
        textInput.TextInputHandler!(textEvent);

        Assert.That(textEvent.Consumed, Is.False);
    }

    [Test]
    public void ARootThatNeverFocusesAnything_AsksThePlatformForNothing()
    {
        FakeTextInputService textInput = new();
        UiRoot root = Bridged(textInput, out RecordingFocusTarget _);

        root.PointerPressed(new Vector2Int(300, 200), MouseButton.Left);

        Assert.That(textInput.Calls, Is.Empty);
    }

    [Test]
    public void FocusingAField_StartsTextInputForItsScope()
    {
        FakeTextInputService textInput = new();
        UiRoot root = Bridged(textInput, out RecordingFocusTarget field, new ViewScope(2));

        root.Focus(field);

        Assert.That(textInput.Calls, Is.EqualTo(new[] { $"start {new ViewScope(2)}" }));
    }

    [Test]
    public void LosingFocus_StopsTextInput()
    {
        FakeTextInputService textInput = new();
        UiRoot root = Bridged(textInput, out RecordingFocusTarget field);
        root.Focus(field);
        textInput.Calls.Clear();

        root.Focus(null);

        Assert.That(textInput.Calls, Is.EqualTo(new[] { $"stop {default(ViewScope)}" }));
    }

    [Test]
    public void HandingFocusFromOneFieldToAnother_DoesNotStopAndStartInBetween()
    {
        FakeTextInputService textInput = new();
        RecordingFocusTarget first = Sized();
        RecordingFocusTarget second = Sized();
        UiRoot root = Bridged(textInput, new Row { Children = { first, second } });
        root.Focus(first);
        textInput.Calls.Clear();

        root.Focus(second);

        Assert.That(textInput.Calls, Is.Empty, "focus never went away, so the platform has nothing to do");
    }

    [Test]
    public void RemovingTheLayerHoldingFocus_StopsTextInputStraightAway()
    {
        FakeTextInputService textInput = new();
        RecordingFocusTarget field = Sized();
        Element layer = new Column { Children = { field } };
        UiRoot root = Bridged(textInput, layer);
        root.Focus(field);
        textInput.Calls.Clear();

        root.RemoveLayer(layer);

        Assert.That(textInput.Calls, Is.EqualTo(new[] { $"stop {default(ViewScope)}" }),
            "a closed window runs no further pass, so waiting for one would leave text input on for good");
    }

    private static RecordingFocusTarget Sized() =>
        new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };

    private static UiRoot Bridged(FakeTextInputService textInput, out RecordingFocusTarget field, ViewScope viewScope = default)
    {
        field = Sized();
        return Bridged(textInput, new Column { Children = { field } }, viewScope);
    }

    private static UiRoot Bridged(FakeTextInputService textInput, Element layer, ViewScope viewScope = default) =>
        Bridged(textInput, new FakeKeyboardService(), layer, viewScope);

    private static UiRoot Bridged(
        FakeTextInputService textInput,
        FakeKeyboardService keyboard,
        Element layer,
        ViewScope viewScope = default,
        int inputOrder = 0)
    {
        UiRoot root = new();
        root.AddLayer(layer);
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        // Constructing it is what subscribes it; nothing holds the result because the subscriptions
        // are the whole of what it does.
        _ = new UiInputSystem(
            root,
            () => new Size<uint>(320, 240),
            viewScope,
            inputOrder,
            new SilentMouseService(),
            keyboard,
            textInput);

        return root;
    }
}
