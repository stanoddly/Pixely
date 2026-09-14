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
        int inputOrder = 0) =>
        Bridged(textInput, keyboard, new FakeMouseService(), layer, viewScope, inputOrder);

    private static UiRoot Bridged(
        FakeTextInputService textInput,
        FakeKeyboardService keyboard,
        FakeMouseService mouse,
        Element layer,
        ViewScope viewScope = default,
        int inputOrder = 0,
        Size<uint>? windowSize = null)
    {
        UiRoot root = new();
        root.AddLayer(layer);
        root.SetTargetSize(new Vector2Int(320, 240));
        root.Update();

        // Constructing it is what subscribes it; nothing holds the result because the subscriptions
        // are the whole of what it does.
        _ = new UiInputSystem(
            root,
            () => windowSize ?? new Size<uint>(320, 240),
            viewScope,
            inputOrder,
            mouse,
            keyboard,
            textInput);

        return root;
    }

    [Test]
    public void AWheel_ReachesTheScrollViewUnderItAndIsConsumed()
    {
        FakeMouseService mouse = new();
        ScrollView scrollView = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(50), Children = { new MeasuredBox(100, 500) } };
        Bridged(new FakeTextInputService(), new FakeKeyboardService(), mouse, new Column { Children = { scrollView } }, new ViewScope(3), inputOrder: -7);

        MouseWheelEventArgs wheelEvent = new() { Delta = new System.Numerics.Vector2(0f, -1f), Position = new System.Numerics.Vector2(10f, 10f) };
        mouse.WheelHandler!(wheelEvent);

        Assert.Multiple(() =>
        {
            Assert.That(scrollView.ScrollOffset, Is.EqualTo(new Vector2Int(0, 40)));
            Assert.That(wheelEvent.Consumed, Is.True);
            Assert.That(mouse.WheelSubscription, Is.EqualTo((new ViewScope(3), -7)), "subscribed for its own window, at the order it was given");
        });
    }

    [Test]
    public void AWheel_IsScaledFromWindowToViewportCoordinates()
    {
        FakeMouseService mouse = new();
        ScrollView scrollView = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(50), Children = { new MeasuredBox(100, 500) } };
        UiRoot root = Bridged(new FakeTextInputService(), new FakeKeyboardService(), mouse, new Column { Children = { scrollView } }, windowSize: new Size<uint>(640, 480));

        // Half way across a window twice the viewport's size is half way across the viewport.
        mouse.WheelHandler!(new MouseWheelEventArgs { Delta = new System.Numerics.Vector2(0f, -1f), Position = new System.Numerics.Vector2(100f, 40f) });

        Assert.Multiple(() =>
        {
            Assert.That(root.PointerPosition, Is.EqualTo(new Vector2Int(50, 20)));
            Assert.That(scrollView.ScrollOffset, Is.EqualTo(new Vector2Int(0, 40)));
        });
    }

    [Test]
    public void AWheelOverNothingThatScrolls_IsLeftForTheGame()
    {
        FakeMouseService mouse = new();
        Bridged(new FakeTextInputService(), new FakeKeyboardService(), mouse, new Column { Children = { Sized() } });

        MouseWheelEventArgs wheelEvent = new() { Delta = new System.Numerics.Vector2(0f, -1f), Position = new System.Numerics.Vector2(10f, 10f) };
        mouse.WheelHandler!(wheelEvent);

        Assert.That(wheelEvent.Consumed, Is.False);
    }
}
