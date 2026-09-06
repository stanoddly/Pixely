using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// Focus decides which element keys reach, and — because a field commits when it loses focus — the
/// order in which a click's effects happen. Most of these are about the transition surviving a
/// callback that changes the tree while the transition is still in progress.
/// </summary>
public class FocusTests
{
    private static readonly Keyboard NoModifiers = new();

    [Test]
    public void PressingAFocusTarget_FocusesIt()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.SameAs(field));
            Assert.That(field.Calls, Is.EqualTo(new[] { "focused" }));
        });
    }

    [Test]
    public void PressingItAgain_DoesNotCommitAndReopenIt()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Left);
        field.Calls.Clear();

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);

        Assert.That(field.Calls, Is.Empty);
    }

    [Test]
    public void PressingSomethingElse_BlursBeforeThatElementIsPressed()
    {
        RecordingFocusTarget field = Sized();
        RecordingPointerTarget button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { field, button } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);

        // The order is the point: a field that commits on blur has to have written its value before
        // whatever was clicked next reads it.
        List<string> order = new();
        field.WhenBlurred = () => order.Add("committed");
        button.WhenPressed = () => order.Add("pressed");

        root.PointerPressed(new Vector2Int(50, 10), MouseButton.Left);

        Assert.That(order, Is.EqualTo(new[] { "pressed", "committed" }),
            "the press is what decides focus moved at all, so the commit lands immediately after it and well before any click on release");
    }

    [Test]
    public void PressingNothing_TakesFocusAway()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Left);
        field.Calls.Clear();

        root.PointerPressed(new Vector2Int(300, 200), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.Null);
            Assert.That(field.Calls, Is.EqualTo(new[] { "blurred" }), "clicking the background commits what was being edited");
        });
    }

    [Test]
    public void ADeclinedPress_TakesFocusAway()
    {
        RecordingFocusTarget field = Sized();
        RecordingPointerTarget other = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { field, other } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Left);
        field.Calls.Clear();

        other.Accepts.Clear();
        root.PointerPressed(new Vector2Int(50, 10), MouseButton.Left);

        Assert.That(field.Calls, Is.EqualTo(new[] { "blurred" }));
    }

    [Test]
    public void ARightPressOnTheBackground_LeavesFocusAlone()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Left);
        field.Calls.Clear();

        root.PointerPressed(new Vector2Int(300, 200), MouseButton.Right);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.SameAs(field));
            Assert.That(field.Calls, Is.Empty, "only the left button moves focus");
        });
    }

    [Test]
    public void ARightPressOnAnotherFocusTarget_LeavesFocusAlone()
    {
        RecordingFocusTarget field = Sized();
        RecordingFocusTarget other = Sized();
        other.Accepts.Add(MouseButton.Right);
        UiRoot root = new();
        root.AddLayer(new Row { Children = { field, other } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        root.Focus(field);
        field.Calls.Clear();

        // Accepted, and on something that could hold focus — the button is the only thing keeping it
        // where it is, so a right-click menu does not take the caret out of a field.
        root.PointerPressed(new Vector2Int(50, 10), MouseButton.Right);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.SameAs(field));
            Assert.That(other.Calls, Does.Not.Contain("focused"));
        });
    }

    [Test]
    public void AKey_ReachesTheFocusedElementAndIsConsumed()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.Focus(field);
        field.Calls.Clear();

        bool consumed = root.KeyPressed(Scancode.A, NoModifiers, isRepeat: true);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True);
            Assert.That(field.Calls, Is.EqualTo(new[] { "key A repeat" }));
        });
    }

    [Test]
    public void AKeyWithNothingFocused_IsNotConsumed()
    {
        UiRoot root = Rooted(Sized());

        Assert.Multiple(() =>
        {
            Assert.That(root.KeyPressed(Scancode.A, NoModifiers), Is.False);
            Assert.That(root.TextEntered("a"), Is.False);
        });
    }

    [Test]
    public void AKeyTheElementDoesNotWant_IsNotConsumed()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.Focus(field);
        field.HandlesKeys = false;

        Assert.That(root.KeyPressed(Scancode.A, NoModifiers), Is.False,
            "an unused key still has to reach the game underneath");
    }

    [Test]
    public void FocusingSomethingThatIsNotAFocusTarget_TakesFocusAway()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.Focus(field);

        root.Focus(new Element());

        Assert.That(root.FocusedElement, Is.Null);
    }

    [Test]
    public void HidingTheFocusedElement_BlursItOnTheNextPass()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.Focus(field);
        field.Calls.Clear();

        field.IsVisible = false;
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.Null);
            Assert.That(field.Calls, Is.EqualTo(new[] { "blurred" }));
        });
    }

    [Test]
    public void AKeyAfterTheFocusedElementLeftTheTree_ReachesNothing()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.Focus(field);

        // No pass in between, so nothing has had the chance to reconcile: the dispatch itself has to
        // notice, or a key goes to an element that is no longer on screen.
        field.IsVisible = false;
        field.Calls.Clear();
        bool consumed = root.KeyPressed(Scancode.A, NoModifiers);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(field.Calls, Is.EqualTo(new[] { "blurred" }));
        });
    }

    [Test]
    public void ABlurHandlerThatFocusesSomethingElse_Wins()
    {
        RecordingFocusTarget first = Sized();
        RecordingFocusTarget second = Sized();
        UiRoot root = new();
        root.AddLayer(new Row { Children = { first, second } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        root.Focus(first);

        first.WhenBlurred = () =>
        {
            first.WhenBlurred = null;
            root.Focus(second);
        };

        root.Focus(null);

        Assert.That(root.FocusedElement, Is.SameAs(second),
            "the nested transition is the current answer, not something the outer one overwrites");
    }

    [Test]
    public void ABlurHandlerThatDetachesTheIncomingElement_LeavesNothingFocused()
    {
        RecordingFocusTarget first = Sized();
        RecordingFocusTarget second = Sized();
        UiRoot root = new();
        root.AddLayer(new Row { Children = { first, second } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        root.Focus(first);

        // Committing rebuilt the thing focus was heading for, which is the ordinary shape of this:
        // a field writes its value and the view rebuilds the row it belonged to.
        first.WhenBlurred = () => second.IsVisible = false;

        root.Focus(second);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.Null);
            Assert.That(second.Calls, Is.Empty, "an element that is gone is never told it gained focus");
        });
    }

    [Test]
    public void AFocusHandlerThatDetachesItsOwnElement_DoesNotKeepFocus()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);

        field.WhenFocused = () => field.IsVisible = false;

        root.Focus(field);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.Null);
            Assert.That(field.Calls, Is.EqualTo(new[] { "focused", "blurred" }));
        });
    }

    [Test]
    public void FocusChanged_ReportsTheSettledElementOnce()
    {
        RecordingFocusTarget first = Sized();
        RecordingFocusTarget second = Sized();
        UiRoot root = new();
        root.AddLayer(new Row { Children = { first, second } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        List<Element?> reported = new();
        root.FocusChanged += reported.Add;

        // A field handing focus straight on must not look like focus went away and came back, or what
        // starts and stops the platform's text input would stop and start it in between.
        first.WhenBlurred = () =>
        {
            first.WhenBlurred = null;
            root.Focus(second);
        };

        root.Focus(first);
        root.Focus(null);

        Assert.That(reported, Is.EqualTo(new Element?[] { first, second }));
    }

    [Test]
    public void ABlurHandlerThatTakesFocusAway_IsNotOverriddenByTheHandoff()
    {
        RecordingFocusTarget first = Sized();
        RecordingFocusTarget second = Sized();
        UiRoot root = InRow(first, second);
        root.Focus(first);

        // Asking for focus it already has - none - is still saying where focus belongs. A handoff
        // that carried on because that looked like nothing would move focus somewhere this refused.
        first.WhenBlurred = () =>
        {
            first.WhenBlurred = null;
            root.Focus(null);
        };

        root.Focus(second);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.Null);
            Assert.That(second.Calls, Does.Not.Contain("focused"));
        });
    }

    [Test]
    public void ABlurHandlerThatDetachesWhatItJustFocused_LeavesNothingFocused()
    {
        RecordingFocusTarget first = Sized();
        RecordingFocusTarget second = Sized();
        RecordingFocusTarget third = Sized();
        UiRoot root = InRow(first, second, third);
        root.Focus(first);

        // The nested transition finishes and only then is its element taken away, so the outer route
        // cannot tell from the version alone that anything is wrong with what it left behind.
        first.WhenBlurred = () =>
        {
            first.WhenBlurred = null;
            root.Focus(third);
            third.IsVisible = false;
        };

        root.Focus(second);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.Null);
            Assert.That(third.Calls, Is.EqualTo(new[] { "focused", "blurred" }));
        });
    }

    [Test]
    public void AKeyWhoseValidationFocusesSomethingThenDetachesIt_ReachesNothing()
    {
        RecordingFocusTarget stale = Sized();
        RecordingFocusTarget replacement = Sized();
        UiRoot root = InRow(stale, replacement);
        root.Focus(stale);

        // Dispatching first has to notice the focused element is gone; blurring it is what focuses
        // the replacement, and that replacement is taken away before the key is delivered.
        stale.WhenBlurred = () =>
        {
            stale.WhenBlurred = null;
            root.Focus(replacement);
            replacement.IsVisible = false;
        };

        stale.IsVisible = false;
        bool consumed = root.KeyPressed(Scancode.A, NoModifiers);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(replacement.Calls, Does.Not.Contain("key A"));
        });
    }

    [Test]
    public void RemovingTheLayerHoldingFocus_BlursItStraightAway()
    {
        RecordingFocusTarget field = Sized();
        Element layer = new Column { Children = { field } };
        UiRoot root = new();
        root.AddLayer(layer);
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        root.Focus(field);

        List<Element?> reported = new();
        root.FocusChanged += reported.Add;

        // No pass follows a window being closed, so waiting for one would leave the platform's text
        // input running for a field that is gone.
        root.RemoveLayer(layer);

        Assert.Multiple(() =>
        {
            Assert.That(root.FocusedElement, Is.Null);
            Assert.That(field.Calls, Does.Contain("blurred"));
            Assert.That(reported, Is.EqualTo(new Element?[] { null }));
        });
    }

    [Test]
    public void AFocusSubscriberThatDetachesWhatItWasTold_IsToldAgain()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);

        List<Element?> reported = new();
        bool detached = false;
        root.FocusChanged += focused =>
        {
            reported.Add(focused);

            if (focused == null || detached)
            {
                return;
            }

            detached = true;
            field.IsVisible = false;
        };

        root.Focus(field);

        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.EqualTo(new Element?[] { field, null }));
            Assert.That(root.FocusedElement, Is.Null, "text input must not be left running for it");
        });
    }

    [Test]
    public void AnUpdateFromInsideABlurHandler_DoesNotReportAHandoffAsTwoMoves()
    {
        RecordingFocusTarget first = Sized();
        RecordingFocusTarget second = Sized();
        UiRoot root = InRow(first, second);
        root.Focus(first);

        List<Element?> reported = new();
        root.FocusChanged += reported.Add;

        // A field that commits by rebuilding its view changes the tree and runs a pass, all while
        // focus is between owners.
        first.WhenBlurred = () =>
        {
            first.WhenBlurred = null;
            second.Margin = new Thickness(1);
            root.Update();
        };

        root.Focus(second);

        Assert.That(reported, Is.EqualTo(new Element?[] { second }),
            "reported once, so nothing stops and restarts the platform's text input mid-handoff");
    }

    [Test]
    public void ABlurHandlerThatThrows_StillReportsThatFocusWent()
    {
        RecordingFocusTarget field = Sized();
        UiRoot root = Rooted(field);
        root.Focus(field);

        List<Element?> reported = new();
        root.FocusChanged += reported.Add;
        field.WhenBlurred = () => throw new InvalidOperationException("commit failed");

        Assert.Throws<InvalidOperationException>(() => root.Focus(null));

        Assert.That(reported, Is.EqualTo(new Element?[] { null }),
            "focus has already gone, and leaving that unsaid leaves text input running for nothing");
    }

    private static UiRoot InRow(params Element[] children)
    {
        Row row = new();

        foreach (Element child in children)
        {
            row.Children.Add(child);
        }

        UiRoot root = new();
        root.AddLayer(row);
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }

    private static RecordingFocusTarget Sized() =>
        new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };

    private static UiRoot Rooted(Element content)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { content } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }
}
