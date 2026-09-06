using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// The bridge between the platform's input services and the root. Only the text-input decision is
/// exercised here — whether the platform is asked to start and stop, and when — because the rest of
/// the bridge is forwarding whose event arguments a test cannot construct.
/// </summary>
public class UiInputSystemTests
{
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

    private static UiRoot Bridged(FakeTextInputService textInput, Element layer, ViewScope viewScope = default)
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
            inputOrder: 0,
            new SilentMouseService(),
            new SilentKeyboardService(),
            textInput);

        return root;
    }
}
