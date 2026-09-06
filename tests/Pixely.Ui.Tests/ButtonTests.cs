using Pixely.Gpu;

namespace Pixely.Ui.Tests;

/// <summary>
/// A button is the first element that reacts to input, so these cover the routing as much as the
/// button: what the root considers hit, which element wins when several overlap, and which press
/// and release pairs count as a click.
/// </summary>
public class ButtonTests
{
    [Test]
    public void PressAndReleaseInside_Clicks()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);
        int clicks = 0;
        button.Clicked += () => clicks++;

        root.PointerPressed(new Vector2Int(10, 10));
        root.PointerReleased(new Vector2Int(10, 10));

        Assert.That(clicks, Is.EqualTo(1));
    }

    [Test]
    public void ReleaseOutside_DoesNotClick()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);
        int clicks = 0;
        button.Clicked += () => clicks++;

        root.PointerPressed(new Vector2Int(10, 10));
        root.PointerMoved(new Vector2Int(200, 200));
        root.PointerReleased(new Vector2Int(200, 200));

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero);
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));
        });
    }

    [Test]
    public void PressDraggedOffAndBack_StillClicks()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);
        int clicks = 0;
        button.Clicked += () => clicks++;

        root.PointerPressed(new Vector2Int(10, 10));
        Assert.That(button.VisualState, Is.EqualTo(VisualState.Pressed));

        root.PointerMoved(new Vector2Int(200, 200));
        Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal), "a press dragged away shows that releasing there will not click");

        root.PointerMoved(new Vector2Int(12, 12));
        Assert.That(button.VisualState, Is.EqualTo(VisualState.Pressed));

        root.PointerReleased(new Vector2Int(12, 12));

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1));
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Hovered));
        });
    }

    [Test]
    public void Hovering_ChangesTheVisualState()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);

        Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));

        root.PointerMoved(new Vector2Int(10, 10));
        Assert.That(button.VisualState, Is.EqualTo(VisualState.Hovered));

        root.PointerMoved(new Vector2Int(200, 10));
        Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));
    }

    [Test]
    public void PointerLeavingTheWindow_CancelsThePress()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);
        int clicks = 0;
        button.Clicked += () => clicks++;

        root.PointerPressed(new Vector2Int(10, 10));
        root.PointerLeft();

        Assert.Multiple(() =>
        {
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));
            Assert.That(root.PointerReleased(new Vector2Int(10, 10)), Is.False, "the release ends nothing, so it is not the UI's to consume");
            Assert.That(clicks, Is.Zero);
        });
    }

    [Test]
    public void ADisabledButton_IsNotHitAtAll()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20), IsEnabled = false };
        UiRoot root = Rooted(button);
        int clicks = 0;
        button.Clicked += () => clicks++;

        bool pressed = root.PointerPressed(new Vector2Int(10, 10));
        root.PointerReleased(new Vector2Int(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.False);
            Assert.That(clicks, Is.Zero);
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Disabled));
        });
    }

    [Test]
    public void AButtonUnderADisabledAncestor_IsNotHit()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        Column panel = new() { Children = { button }, IsEnabled = false };
        UiRoot root = Rooted(panel);

        Assert.Multiple(() =>
        {
            Assert.That(root.PointerPressed(new Vector2Int(10, 10)), Is.False);
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Disabled));
        });
    }

    [Test]
    public void AnInvisibleButton_IsNotHit()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20), IsVisible = false };
        UiRoot root = Rooted(button);

        Assert.That(root.PointerPressed(new Vector2Int(10, 10)), Is.False);
    }

    [Test]
    public void WhereTwoButtonsOverlap_TheOneOnTopIsHit()
    {
        Button under = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        Button over = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        Overlay overlay = new() { Children = { under, over } };
        UiRoot root = Rooted(overlay);

        root.PointerPressed(new Vector2Int(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(over.VisualState, Is.EqualTo(VisualState.Pressed), "the later child is painted on top, so it takes the pointer");
            Assert.That(under.VisualState, Is.EqualTo(VisualState.Normal));
        });
    }

    [Test]
    public void ALaterLayer_TakesThePointerFromAnEarlierOne()
    {
        Button background = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        Button dialog = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new();
        root.AddLayer(new Column { Children = { background } });
        root.AddLayer(new Column { Children = { dialog } });
        Update(root);

        root.PointerPressed(new Vector2Int(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(dialog.VisualState, Is.EqualTo(VisualState.Pressed));
            Assert.That(background.VisualState, Is.EqualTo(VisualState.Normal));
        });
    }

    [Test]
    public void ThePartOfAButtonClippedAway_IsNotHit()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(40) };
        ClipBorder clip = new() { Content = button, Width = Sizing.Fixed(40), Height = Sizing.Fixed(10) };
        UiRoot root = Rooted(clip);

        Assert.Multiple(() =>
        {
            Assert.That(button.Bounds.Height, Is.EqualTo(40), "the button still occupies its whole area");
            Assert.That(root.PointerPressed(new Vector2Int(10, 5)), Is.True);
            Assert.That(root.PointerPressed(new Vector2Int(10, 25)), Is.False, "below the clip the button is not visible, so it is not clickable");
        });
    }

    [Test]
    public void AdjacentButtons_DoNotShareTheirEdge()
    {
        Button left = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Button right = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Row row = new() { Children = { left, right } };
        UiRoot root = Rooted(row);

        root.PointerMoved(new Vector2Int(20, 5));

        Assert.Multiple(() =>
        {
            Assert.That(left.VisualState, Is.EqualTo(VisualState.Normal));
            Assert.That(right.VisualState, Is.EqualTo(VisualState.Hovered));
        });
    }

    [Test]
    public void HoveringRepaints()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);

        Assert.That(root.Update(), Is.False, "nothing changed yet");

        root.PointerMoved(new Vector2Int(10, 10));

        Assert.That(root.Update(), Is.True, "the hovered background has to reach the screen");
    }

    [Test]
    public void TheBackgroundFollowsTheState()
    {
        SolidDrawable normal = new(Colors.Red);
        SolidDrawable hovered = new(Colors.Green);
        Button button = new()
        {
            Width = Sizing.Fixed(40),
            Height = Sizing.Fixed(20),
            Backgrounds = new StateDrawables(normal) { Hovered = hovered }
        };
        UiRoot root = Rooted(button);

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Red));

        root.PointerMoved(new Vector2Int(10, 10));
        root.Update();

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Green));
    }

    [Test]
    public void WithoutItsOwnBackgrounds_AButtonTakesTheStyles()
    {
        SolidDrawable styled = new(Colors.Blue);
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new() { Style = new UiStyle { ButtonBackground = new StateDrawables(styled) } };
        root.AddLayer(new Column { Children = { button } });
        Update(root);

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Blue));
    }

    [Test]
    public void AStateWithoutADrawable_FallsBackToNormal()
    {
        StateDrawables backgrounds = new(new SolidDrawable(Colors.Red)) { Pressed = new SolidDrawable(Colors.Green) };

        Assert.Multiple(() =>
        {
            Assert.That(backgrounds.Resolve(VisualState.Pressed), Is.SameAs(backgrounds.Pressed));
            Assert.That(backgrounds.Resolve(VisualState.Hovered), Is.SameAs(backgrounds.Normal));
            Assert.That(backgrounds.Resolve(VisualState.Disabled), Is.SameAs(backgrounds.Normal));
        });
    }

    [Test]
    public void ATreeThatWasNeverLaidOut_HitsNothing()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new();
        root.AddLayer(button);

        Assert.That(root.PointerPressed(new Vector2Int(10, 10)), Is.False);
    }

    /// <summary>
    /// Roots <paramref name="content"/> under a layer rather than as one, because a layer is
    /// arranged at the full viewport and would leave a button covering the whole screen.
    /// </summary>
    [Test]
    public void PressingASecondButton_CancelsTheFirstsPress()
    {
        Button first = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Button second = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(new Row { Children = { first, second } });
        int clicks = 0;
        first.Clicked += () => clicks++;

        root.PointerPressed(new Vector2Int(5, 5));
        root.PointerPressed(new Vector2Int(25, 5));
        root.PointerReleased(new Vector2Int(25, 5));

        // Hovering the first button again is what exposes a press it was never told had ended: a
        // button that is not hovered reads as normal whether or not it still believes it is held.
        root.PointerMoved(new Vector2Int(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero);
            Assert.That(first.VisualState, Is.EqualTo(VisualState.Hovered), "the abandoned press must not leave the first button pressed for good");
            Assert.That(second.VisualState, Is.EqualTo(VisualState.Normal));
        });
    }

    [Test]
    public void AButtonMovedOutFromUnderAStillPointer_StopsBeingHovered()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Column layer = new() { Children = { button } };
        UiRoot root = Rooted(layer);

        root.PointerMoved(new Vector2Int(5, 5));
        Assert.That(button.VisualState, Is.EqualTo(VisualState.Hovered));

        layer.Padding = new Thickness(100, 0, 0, 0);
        root.Update();

        Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal), "the pointer did not move, but the button did");
    }

    [Test]
    public void AButtonMovedUnderAStillPointer_BecomesHovered()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Column layer = new() { Children = { button }, Padding = new Thickness(100, 0, 0, 0) };
        UiRoot root = Rooted(layer);

        root.PointerMoved(new Vector2Int(5, 5));
        Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));

        layer.Padding = Thickness.Zero;
        root.Update();

        Assert.That(button.VisualState, Is.EqualTo(VisualState.Hovered));
    }

    [Test]
    public void RemovingAHoveredButton_LeavesIt()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Column layer = new() { Children = { button } };
        UiRoot root = Rooted(layer);

        root.PointerMoved(new Vector2Int(5, 5));
        layer.Children.Remove(button);
        root.Update();

        Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));
    }

    [Test]
    public void RemovingACapturedButton_CancelsThePress()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Column layer = new() { Children = { button } };
        UiRoot root = Rooted(layer);
        int clicks = 0;
        button.Clicked += () => clicks++;

        root.PointerPressed(new Vector2Int(5, 5));
        layer.Children.Remove(button);
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));
            Assert.That(root.PointerReleased(new Vector2Int(5, 5)), Is.False, "capture ended with the removal, so the release ends nothing");
            Assert.That(clicks, Is.Zero);
        });
    }

    [Test]
    public void AClickThatReplacesTheButton_HoversTheReplacement()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Button replacement = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Column layer = new() { Children = { button } };
        UiRoot root = Rooted(layer);

        button.Clicked += () =>
        {
            layer.Children.Clear();
            layer.Children.Add(replacement);
        };

        root.PointerPressed(new Vector2Int(5, 5));
        root.PointerReleased(new Vector2Int(5, 5));

        Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal), "hover is recomputed after the callback, not from the hit that preceded it");

        // The replacement was not in the tree when the targets were collected, so it takes the
        // pointer at the build that puts it there.
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(replacement.VisualState, Is.EqualTo(VisualState.Hovered));
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Normal));
        });
    }

    [Test]
    public void LeavingTheWindow_CancelsRatherThanFakingARelease()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(5, 5));
        root.PointerLeft();

        Assert.That(target.Calls, Is.EqualTo(new[] { "enter 5,5", "press 5,5", "cancel", "leave" }));
    }

    [Test]
    public void AClick_ReportsItsCallbacksInOrder()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(target);

        root.PointerMoved(new Vector2Int(5, 5));
        root.PointerPressed(new Vector2Int(6, 6));
        root.PointerReleased(new Vector2Int(7, 7));

        Assert.That(target.Calls, Is.EqualTo(new[] { "enter 5,5", "press 6,6", "release 7,7 inside=True" }));
    }

    [Test]
    public void TheInheritedBackground_IsUsedForEveryStateWhenNoStateBackgroundsAreGiven()
    {
        Button button = new()
        {
            Width = Sizing.Fixed(40),
            Height = Sizing.Fixed(20),
            Background = new SolidDrawable(Colors.Red)
        };
        UiRoot root = Rooted(button);

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Red));

        root.PointerMoved(new Vector2Int(10, 10));
        root.Update();

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Red), "one drawable means one look, not a hover that reverts to the default");
    }

    [Test]
    public void StateBackgrounds_WinOverTheInheritedOne()
    {
        Button button = new()
        {
            Width = Sizing.Fixed(40),
            Height = Sizing.Fixed(20),
            Background = new SolidDrawable(Colors.Red),
            Backgrounds = new StateDrawables(new SolidDrawable(Colors.Blue))
        };
        UiRoot root = Rooted(button);

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Blue));
    }

    [Test]
    public void ReplacingTheStyle_Repaints()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new() { Style = new UiStyle { ButtonBackground = new StateDrawables(new SolidDrawable(Colors.Red)) } };
        root.AddLayer(new Column { Children = { button } });
        Update(root);

        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Red));

        root.Style = new UiStyle { ButtonBackground = new StateDrawables(new SolidDrawable(Colors.Blue)) };

        Assert.Multiple(() =>
        {
            Assert.That(root.Update(), Is.True);
            Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Blue));
        });
    }

    [Test]
    public void AMovedButton_IsPaintedInTheStateItEndsUpIn()
    {
        Button button = new()
        {
            Width = Sizing.Fixed(20),
            Height = Sizing.Fixed(20),
            Backgrounds = new StateDrawables(new SolidDrawable(Colors.Red)) { Hovered = new SolidDrawable(Colors.Green) }
        };
        Column layer = new() { Children = { button } };
        UiRoot root = Rooted(layer);

        root.PointerMoved(new Vector2Int(5, 5));
        root.Update();
        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Green));

        layer.Padding = new Thickness(100, 0, 0, 0);
        root.Update();

        // Revalidation runs between arrange and paint, so the frame that moves the button is
        // already the frame that stops painting it hovered.
        Assert.That(PaintedColor(root), Is.EqualTo((FColor)Colors.Red));
    }

    [Test]
    public void DisablingACapturedButton_CancelsThePressRatherThanResumingIt()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);
        int clicks = 0;
        button.Clicked += () => clicks++;

        root.PointerPressed(new Vector2Int(5, 5));
        button.IsEnabled = false;
        root.Update();

        button.IsEnabled = true;
        root.Update();
        root.PointerReleased(new Vector2Int(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero, "the press did not survive the button being unreachable");
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Hovered));
        });
    }

    [Test]
    public void HidingACapturedButton_CancelsThePress()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(5, 5));
        target.IsVisible = false;
        root.Update();

        Assert.That(target.Calls, Is.EqualTo(new[] { "enter 5,5", "press 5,5", "cancel", "leave" }));
    }

    [Test]
    public void AReleaseWithoutAPress_StillUpdatesHover()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);

        Assert.That(root.PointerReleased(new Vector2Int(5, 5)), Is.False);
        Assert.That(button.VisualState, Is.EqualTo(VisualState.Hovered), "the pointer is over the button whether or not the release ended anything");
    }

    [Test]
    public void ALeaveCallbackThatRoutesAgain_IsNotUndoneByItsCaller()
    {
        RecordingPointerTarget first = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        RecordingPointerTarget second = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(new Row { Children = { first, second } });

        root.PointerMoved(new Vector2Int(5, 5));
        first.WhenLeft = () => root.PointerLeft();

        root.PointerMoved(new Vector2Int(25, 5));

        Assert.That(second.Calls, Is.Empty, "the nested route left the window, so nothing is hovered when it returns");
    }

    [Test]
    public void APressWhoseLeaveCallbackReroutes_DoesNotCaptureTheNewTarget()
    {
        RecordingPointerTarget first = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        RecordingPointerTarget second = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(new Row { Children = { first, second } });

        root.PointerMoved(new Vector2Int(5, 5));
        first.WhenLeft = () => root.PointerLeft();

        bool pressed = root.PointerPressed(new Vector2Int(25, 5));

        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.False);
            Assert.That(second.Calls, Is.Empty, "a press must not land on a target the current route has moved away from");
        });
    }

    [Test]
    public void ALeaveCallbackThatDetachesTheDestination_DoesNotEnterIt()
    {
        RecordingPointerTarget first = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        RecordingPointerTarget second = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Row row = new() { Children = { first, second } };
        UiRoot root = Rooted(row);

        root.PointerMoved(new Vector2Int(5, 5));
        first.WhenLeft = () => row.Children.Remove(second);

        root.PointerMoved(new Vector2Int(25, 5));

        Assert.That(second.Calls, Is.Empty);
    }

    [Test]
    public void UpdatingFromAPointerCallbackTheUpdateItselfFired_IsRefused()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Column layer = new() { Children = { target }, Padding = new Thickness(100, 0, 0, 0) };
        UiRoot root = Rooted(layer);
        bool nestedUpdated = true;

        root.PointerMoved(new Vector2Int(5, 5));

        // Reconciling hover happens inside Update, so an enter that updates again re-enters it.
        target.WhenEntered = () =>
        {
            target.Background = new SolidDrawable(Colors.Red);
            nestedUpdated = root.Update();
        };

        layer.Padding = Thickness.Zero;
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(nestedUpdated, Is.False);
            Assert.That(root.Instructions, Has.Count.EqualTo(1), "one background, painted once, not a tree painted into the same context twice");
        });
    }

    [Test]
    public void AHoverReconciledDuringAnUpdate_DoesNotAskForAnother()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Column layer = new() { Children = { button } };
        UiRoot root = Rooted(layer);

        root.PointerMoved(new Vector2Int(5, 5));
        root.Update();

        layer.Padding = new Thickness(100, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(root.Update(), Is.True);
            Assert.That(root.Update(), Is.False, "the frame that reconciled the hover also painted it");
        });
    }

    [Test]
    public void AButtonOverflowingItsParent_IsStillHitOutsideIt()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(40) };
        Column parent = new() { Children = { button }, Height = Sizing.Fixed(10) };
        UiRoot root = Rooted(parent);

        Assert.Multiple(() =>
        {
            Assert.That(parent.Bounds.Height, Is.EqualTo(10));
            Assert.That(root.PointerPressed(new Vector2Int(5, 30)), Is.True, "clipping is opt-in, so overflowing content is still there to be clicked");
        });
    }

    [Test]
    public void AButtonRemovedSinceTheLastBuild_IsNotHit()
    {
        Button button = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Column layer = new() { Children = { button } };
        UiRoot root = Rooted(layer);

        layer.Children.Remove(button);

        Assert.That(root.PointerPressed(new Vector2Int(5, 5)), Is.False, "the collected areas are a build old, so a candidate is checked against the tree before it counts");
    }

    [Test]
    public void ReorderingFromAPointerCallback_ChangesWhatTheSameFrameCanHit()
    {
        RecordingPointerTarget first = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        RecordingPointerTarget second = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) };
        Overlay overlay = new() { Children = { first, second } };
        Column layer = new() { Children = { overlay }, Padding = new Thickness(100, 0, 0, 0) };
        UiRoot root = Rooted(layer);

        root.PointerMoved(new Vector2Int(5, 5));

        // Entering puts `first` back on top. The paint that follows draws it there, so the press
        // has to land on it rather than on whatever was topmost when the targets were collected.
        second.WhenEntered = () =>
        {
            overlay.Children.Remove(first);
            overlay.Children.Add(first);
        };

        layer.Padding = Thickness.Zero;
        root.Update();

        root.PointerPressed(new Vector2Int(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(second.Calls, Does.Contain("enter 5,5"), "the reorder is what the enter callback did, so it has to have run");
            Assert.That(first.Calls, Does.Contain("press 5,5"));
            Assert.That(second.Calls, Does.Not.Contain("press 5,5"));
        });
    }

    private static UiRoot Rooted(Element content)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { content } });
        Update(root);
        return root;
    }

    private static void Update(UiRoot root)
    {
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
    }

    /// <summary>The tint of the first instruction, which is the button's background.</summary>
    private static FColor PaintedColor(UiRoot root) => root.Instructions[0].Tint;
}
