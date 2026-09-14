using System.Numerics;
using Pixely.Gpu;
using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// What a scroll view does with its children: how it sizes against them, where it slides them,
/// how far it lets the offset go, and what the wheel does to it.
/// </summary>
public class ScrollViewTests
{
    private static readonly Vector2 Down = new(0f, -1f);
    private static readonly Vector2 Up = new(0f, 1f);

    // --- Sizing -------------------------------------------------------------

    [Test]
    public void AFitScrollView_TakesWhatItIsOfferedAndReportsTheExtent()
    {
        ScrollView view = new() { Children = { new MeasuredBox(80, 500) } };

        Vector2Int size = Layout.Measure(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(size, Is.EqualTo(new Vector2Int(80, 120)));
            Assert.That(view.ScrollExtent, Is.EqualTo(new Vector2Int(80, 500)));
        });
    }

    [Test]
    public void AFitScrollViewOnAnUnboundedAxis_GrowsToItsChildrenAndNeverScrolls()
    {
        ScrollView view = new() { Children = { new MeasuredBox(80, 500) } };

        Layout.Run(view, Constraints.Unconstrained, new Rectangle(0, 0, 80, 500));

        Assert.Multiple(() =>
        {
            Assert.That(view.DesiredSize, Is.EqualTo(new Vector2Int(80, 500)));
            Assert.That(view.MaxScrollOffset, Is.EqualTo(default(Vector2Int)));
        });
    }

    [Test]
    public void AGrowChildOnTheScrollAxis_IsMeasuredToItsContent()
    {
        MeasuredBox child = new(80, 500) { Height = Sizing.Grow() };
        ScrollView view = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(120), Children = { child } };

        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(child.DesiredSize, Is.EqualTo(new Vector2Int(80, 500)), "not the 120 the viewport would have been as a budget");
            Assert.That(child.LastConstraints.IsHeightDefinite, Is.False);
        });
    }

    [Test]
    public void AGrowChildOnTheCrossAxis_FillsADefiniteWidthAndDegradesInALooseOne()
    {
        MeasuredBox definite = new(30, 500) { Width = Sizing.Grow() };
        MeasuredBox loose = new(30, 500) { Width = Sizing.Grow() };
        ScrollView definiteView = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(120), Children = { definite } };
        ScrollView looseView = new() { Height = Sizing.Fixed(120), Children = { loose } };

        Layout.Run(definiteView, 100, 120);
        Layout.Measure(looseView, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(definite.DesiredSize.X, Is.EqualTo(100), "as it would in a column of that width");
            Assert.That(loose.DesiredSize.X, Is.EqualTo(30), "as it would in a Fit column");
        });
    }

    [Test]
    public void APercentChildOnTheScrollAxis_Degrades()
    {
        MeasuredBox child = new(80, 500) { Height = Sizing.Percent(0.5f) };
        ScrollView view = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(120), Children = { child } };

        Layout.Run(view, 100, 120);

        Assert.That(child.DesiredSize.Y, Is.EqualTo(500));
    }

    // --- Offset -------------------------------------------------------------

    [Test]
    public void TheOffset_IsClampedWhenBuilt()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.ScrollOffset = new Vector2Int(-10, 1000);

        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(view.MaxScrollOffset, Is.EqualTo(new Vector2Int(0, 380)));
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 380)));
        });
    }

    [Test]
    public void AnOffsetOfIntMaxValue_LandsOnTheEnd()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.ScrollOffset = new Vector2Int(0, int.MaxValue);

        Layout.Run(view, 100, 120);

        Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 380)));
    }

    [Test]
    public void AShrinkingExtent_ReclampsTheOffset()
    {
        MeasuredBox child = new(80, 500);
        ScrollView view = Sized(child);
        view.ScrollOffset = new Vector2Int(0, 380);
        Layout.Run(view, 100, 120);

        child.IntrinsicSize = new Vector2Int(80, 200);
        Layout.Run(view, 100, 120);

        Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 80)));
    }

    [Test]
    public void ADisabledAxis_StaysAtZero()
    {
        ScrollView view = Sized(new MeasuredBox(300, 500));
        view.ScrollOffset = new Vector2Int(50, 50);

        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(view.MaxScrollOffset, Is.EqualTo(new Vector2Int(0, 380)));
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 50)));
        });
    }

    [Test]
    public void ScrollBy_SaturatesRatherThanOverflows()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        Layout.Run(view, 100, 120);

        bool moved = view.ScrollBy(new Vector2Int(0, int.MaxValue));

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 380)));
            Assert.That(view.ScrollBy(new Vector2Int(0, int.MaxValue)), Is.False, "already at the end");
        });
    }

    [Test]
    public void ScrollByAfterADirectAssignmentPastTheEnd_MovesFromTheEnd()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        Layout.Run(view, 100, 120);
        view.ScrollOffset = new Vector2Int(0, int.MaxValue);

        bool inward = view.ScrollBy(new Vector2Int(0, -40));

        Assert.Multiple(() =>
        {
            Assert.That(inward, Is.True);
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 340)), "read as the end, not as a position");
        });
    }

    [Test]
    public void ScrollByAfterADirectAssignmentPastTheEnd_LeavesAnOutwardOrZeroMoveAlone()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        Layout.Run(view, 100, 120);
        view.ScrollOffset = new Vector2Int(0, int.MaxValue);

        Assert.Multiple(() =>
        {
            Assert.That(view.ScrollBy(new Vector2Int(0, 40)), Is.False);
            Assert.That(view.ScrollBy(default), Is.False);
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, int.MaxValue)), "the request is still pending for the build");
        });
    }

    // --- Invalidation ---------------------------------------------------------

    [Test]
    public void Scrolling_MovesTheChildrenAndArrangesWithoutMeasuring()
    {
        MeasuredBox child = new(80, 500);
        ScrollView view = Sized(child);
        Layout.Run(view, 100, 120);
        int measures = child.MeasureCount;

        view.ScrollOffset = new Vector2Int(0, 100);

        Assert.Multiple(() =>
        {
            Assert.That(view.IsMeasureDirty, Is.False);
            Assert.That(view.IsArrangeDirty, Is.True);
        });

        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, -100, 80, 500)));
            Assert.That(child.MeasureCount, Is.EqualTo(measures));
        });
    }

    [Test]
    public void TwoWheelsBeforeABuild_AddUp()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        Layout.Run(view, 100, 120);

        Scroll(view, Down);
        Scroll(view, Down);
        Layout.Run(view, 100, 120);

        Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 80)));
    }

    [Test]
    public void ChangingTheAxes_Remeasures()
    {
        MeasuredBox child = new(300, 500);
        ScrollView view = Sized(child);
        Layout.Run(view, 100, 120);

        view.Axes = ScrollAxes.Both;

        Assert.That(view.IsMeasureDirty, Is.True);

        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(child.LastConstraints.IsWidthBounded, Is.False);
            Assert.That(view.MaxScrollOffset, Is.EqualTo(new Vector2Int(200, 380)));
        });
    }

    [Test]
    public void ChangingTheBars_ArrangesOnly()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        Layout.Run(view, 100, 120);

        view.ScrollBars = ScrollBarVisibility.Hidden;

        Assert.Multiple(() =>
        {
            Assert.That(view.IsMeasureDirty, Is.False);
            Assert.That(view.IsArrangeDirty, Is.True);
        });

        Layout.Run(view, 100, 120);

        Assert.That(VerticalBar(view).Bounds, Is.EqualTo(default(Rectangle)));
    }

    [Test]
    public void ChangingTheWheelStep_DirtiesNothing()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        Layout.Run(view, 100, 120);

        view.WheelStep = 10;

        Assert.Multiple(() =>
        {
            Assert.That(view.IsArrangeDirty, Is.False);
            Assert.That(view.IsPaintDirty, Is.False);
            Assert.That(() => view.WheelStep = 0, Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    // --- Padding ----------------------------------------------------------------

    [Test]
    public void Padding_IsInTheExtentAndScrollsWithTheChildren()
    {
        MeasuredBox child = new(80, 500);
        ScrollView view = Sized(child);
        view.Padding = new Thickness(10);
        view.ScrollOffset = new Vector2Int(0, int.MaxValue);

        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(view.ScrollExtent, Is.EqualTo(new Vector2Int(100, 520)));
            Assert.That(view.MaxScrollOffset, Is.EqualTo(new Vector2Int(0, 400)));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(10, -390, 80, 500)), "the last row has the bottom padding under it");
        });
    }

    [Test]
    public void UnderAnOverlay_AnEndAlignedChildLandsInsideThePaddedBox()
    {
        MeasuredBox child = new(20, 20) { VerticalAlignment = Alignment.End, HorizontalAlignment = Alignment.Center };
        ScrollView view = Sized(child);
        view.Layout = OverlayLayout.Instance;
        view.Padding = new Thickness(10);

        Layout.Run(view, 100, 100);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(40, 70, 20, 20)));
    }

    [Test]
    public void UnderTheDefaultStack_EndAlignmentOnTheScrollAxisDoesNothing()
    {
        MeasuredBox child = new(20, 20) { VerticalAlignment = Alignment.End };
        ScrollView view = Sized(child);
        view.Padding = new Thickness(10);

        Layout.Run(view, 100, 100);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(10, 10, 20, 20)), "a stack gives each child an exact slot, as in a column");
    }

    // --- Layout -------------------------------------------------------------------

    [Test]
    public void AHorizontalStack_MakesAHorizontalStrip()
    {
        MeasuredBox first = new(80, 20);
        MeasuredBox second = new(80, 20);
        ScrollView view = new(gap: 10) { Axes = ScrollAxes.Horizontal, Width = Sizing.Fixed(100), Height = Sizing.Fixed(40), Children = { first, second } };
        view.Layout = new StackLayout(Orientation.Horizontal, 10);
        view.ScrollOffset = new Vector2Int(int.MaxValue, 0);

        Layout.Run(view, 100, 40);

        Assert.Multiple(() =>
        {
            Assert.That(view.ScrollExtent, Is.EqualTo(new Vector2Int(170, 20)));
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(70, 0)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(20, 0, 80, 20)));
        });
    }

    [Test]
    public void SeveralChildren_StackLikeAColumn()
    {
        MeasuredBox first = new(80, 50);
        MeasuredBox second = new(80, 50);
        ScrollView view = new(gap: 4) { Width = Sizing.Fixed(100), Height = Sizing.Fixed(60), Children = { first, second } };

        Layout.Run(view, 100, 60);

        Assert.Multiple(() =>
        {
            Assert.That(view.ScrollExtent, Is.EqualTo(new Vector2Int(80, 104)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(0, 54, 80, 50)));
        });
    }

    // --- Clipping and hit areas ------------------------------------------------------

    [Test]
    public void AChildScrolledOutOfView_CannotBeHitUntilItIsScrolledBack()
    {
        RecordingPointerTarget button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        ScrollView view = Sized(button, new MeasuredBox(80, 500));
        UiRoot root = Rooted(view);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Left);
        Rectangle visible = HitArea(root, button);

        view.ScrollOffset = new Vector2Int(0, 100);
        root.Update();
        Rectangle scrolledAway = HitArea(root, button);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Left);
        int callsWhileAway = button.Calls.Count;

        view.ScrollOffset = default;
        root.Update();
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.EqualTo(new Rectangle(0, 0, 40, 20)));
            Assert.That(scrolledAway, Is.EqualTo(default(Rectangle)), "the button sits at -100, wholly outside the clip");
            Assert.That(button.Calls.Take(callsWhileAway), Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left", "release 10,10 Left inside=True", "leave" }), "nothing reached it while it was scrolled away");
            Assert.That(button.Calls.Skip(callsWhileAway), Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left" }), "and it is hit again once scrolled back");
        });
    }

    [Test]
    public void AChildScrolledIntoView_IsHit()
    {
        RecordingPointerTarget button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        ScrollView view = Sized(new MeasuredBox(80, 500), button);
        UiRoot root = Rooted(view);

        view.ScrollOffset = new Vector2Int(0, int.MaxValue);
        root.Update();
        root.PointerPressed(new Vector2Int(10, 110), MouseButton.Left);

        Assert.That(button.Calls, Is.EqualTo(new[] { "enter 10,110", "press 10,110 Left" }));
    }

    [Test]
    public void TheBar_IsHitBeforeTheChildrenUnderIt()
    {
        RecordingPointerTarget button = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(20) };
        ScrollView view = Sized(button, new MeasuredBox(80, 500));
        UiRoot root = Rooted(view);

        root.PointerPressed(new Vector2Int(97, 10), MouseButton.Left);

        Assert.That(button.Calls, Is.Empty, "the vertical bar lies over the button's right edge");
    }

    // --- Wheel --------------------------------------------------------------------------

    [Test]
    public void AWheelNotch_MovesByTheStep()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.WheelStep = 25;
        Layout.Run(view, 100, 120);

        ScrollAxes taken = Scroll(view, Down);

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.EqualTo(ScrollAxes.Vertical));
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 25)));
        });
    }

    [Test]
    public void Fractions_AddUpAndAreTakenBeforeTheyMove()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.WheelStep = 10;
        Layout.Run(view, 100, 120);

        ScrollAxes first = Scroll(view, new Vector2(0f, -0.05f));
        Vector2Int afterFirst = view.ScrollOffset;
        Scroll(view, new Vector2(0f, -0.05f));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(ScrollAxes.Vertical), "banked, so nothing above gets the same fraction");
            Assert.That(afterFirst, Is.EqualTo(default(Vector2Int)));
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 1)));
        });
    }

    [Test]
    public void ReversingAtAFractionalBoundary_RespondsToTheFirstNotch()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.WheelStep = 10;
        Layout.Run(view, 100, 120);

        // Banks 0.5 of a step at the start, then asks to go further back, which is refused and clears it.
        Scroll(view, new Vector2(0f, -0.05f));
        ScrollAxes outward = Scroll(view, Up);
        Scroll(view, Down);

        Assert.Multiple(() =>
        {
            Assert.That(outward, Is.EqualTo(ScrollAxes.None));
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 10)), "a whole step, not a step plus what was banked");
        });
    }

    [Test]
    public void AnAxisThatDoesNotScroll_IsRefused()
    {
        ScrollView view = Sized(new MeasuredBox(300, 500));
        Layout.Run(view, 100, 120);

        ScrollAxes taken = Scroll(view, new Vector2(1f, -1f));

        Assert.Multiple(() =>
        {
            Assert.That(taken, Is.EqualTo(ScrollAxes.Vertical));
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 40)));
        });
    }

    [Test]
    public void AtTheEnd_AnOutwardWheelIsRefusedAndAnInwardOneTaken()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.ScrollOffset = new Vector2Int(0, int.MaxValue);
        Layout.Run(view, 100, 120);

        ScrollAxes outward = Scroll(view, Down);
        ScrollAxes inward = Scroll(view, Up);

        Assert.Multiple(() =>
        {
            Assert.That(outward, Is.EqualTo(ScrollAxes.None));
            Assert.That(inward, Is.EqualTo(ScrollAxes.Vertical));
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 340)));
        });
    }

    [Test]
    public void AStepTheRangeClamps_DiscardsTheRemainder()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.WheelStep = 10;
        view.ScrollOffset = new Vector2Int(0, 375);
        Layout.Run(view, 100, 120);

        // 1.55 notches is 15.5 pixels: the step of 15 is clamped to 5, and the half is dropped with it.
        Scroll(view, new Vector2(0f, -1.55f));
        Scroll(view, new Vector2(0f, 0.05f));

        Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 380)), "half a notch back has nothing banked to add to");
    }

    [Test]
    public void AssigningTheSameAxesOrStep_KeepsTheRemainder()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.WheelStep = 10;
        Layout.Run(view, 100, 120);
        Scroll(view, new Vector2(0f, -0.05f));

        view.Axes = ScrollAxes.Vertical;
        view.WheelStep = 10;
        Scroll(view, new Vector2(0f, -0.05f));

        Assert.Multiple(() =>
        {
            Assert.That(view.IsMeasureDirty, Is.False, "an unchanged value invalidates nothing");
            Assert.That(view.ScrollOffset, Is.EqualTo(new Vector2Int(0, 1)), "the two halves added up");
        });
    }

    [Test]
    public void ChangingTheAxesOrTheStep_DiscardsTheRemainder()
    {
        ScrollView axes = Sized(new MeasuredBox(80, 500));
        axes.WheelStep = 10;
        ScrollView step = Sized(new MeasuredBox(80, 500));
        step.WheelStep = 10;
        Layout.Run(axes, 100, 120);
        Layout.Run(step, 100, 120);
        Scroll(axes, new Vector2(0f, -0.05f));
        Scroll(step, new Vector2(0f, -0.05f));

        axes.Axes = ScrollAxes.Both;
        step.WheelStep = 20;
        Layout.Run(axes, 100, 120);
        Scroll(axes, new Vector2(0f, -0.05f));
        Scroll(step, new Vector2(0f, -0.025f));

        Assert.Multiple(() =>
        {
            Assert.That(axes.ScrollOffset, Is.EqualTo(default(Vector2Int)));
            Assert.That(step.ScrollOffset, Is.EqualTo(default(Vector2Int)));
        });
    }

    [Test]
    public void ANestedFraction_IsBankedByTheInnerViewOnly()
    {
        ScrollView inner = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50), WheelStep = 10, Children = { new MeasuredBox(50, 500) } };
        ScrollView outer = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), WheelStep = 10, Children = { inner, new MeasuredBox(100, 500) } };
        UiRoot root = Rooted(outer);

        root.PointerScrolled(new Vector2Int(10, 10), new Vector2(0f, -0.05f));
        root.PointerScrolled(new Vector2Int(10, 10), new Vector2(0f, -0.05f));
        root.Update();
        inner.ScrollOffset = new Vector2Int(0, int.MaxValue);
        root.Update();
        root.PointerScrolled(new Vector2Int(10, 10), new Vector2(0f, -0.05f));

        Assert.Multiple(() =>
        {
            Assert.That(inner.ScrollOffset, Is.EqualTo(new Vector2Int(0, 450)));
            Assert.That(outer.ScrollOffset, Is.EqualTo(default(Vector2Int)), "nothing banked from what the inner view took; the last one arrives fresh");
        });
    }

    [Test]
    public void AMixedWheelOverAStripInsideAList_ScrollsEachOnItsOwnAxis()
    {
        ScrollView strip = new() { Axes = ScrollAxes.Horizontal, Width = Sizing.Fixed(50), Height = Sizing.Fixed(30), Children = { new MeasuredBox(500, 30) } };
        ScrollView list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { strip, new MeasuredBox(100, 500) } };
        UiRoot root = Rooted(list);

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), new Vector2(1f, -1f));

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True);
            Assert.That(strip.ScrollOffset, Is.EqualTo(new Vector2Int(40, 0)));
            Assert.That(list.ScrollOffset, Is.EqualTo(new Vector2Int(0, 40)));
        });
    }

    // --- Bars ---------------------------------------------------------------------------

    [Test]
    public void UnderAuto_ABarShowsOnlyWhereTheChildrenOverflow()
    {
        ScrollView fits = Sized(new MeasuredBox(80, 100));
        ScrollView overflows = Sized(new MeasuredBox(80, 500));
        Layout.Run(fits, 100, 120);
        Layout.Run(overflows, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(VerticalBar(fits).Bounds, Is.EqualTo(default(Rectangle)));
            Assert.That(VerticalBar(overflows).Bounds, Is.EqualTo(new Rectangle(94, 0, 6, 120)));
            Assert.That(HorizontalBar(overflows).Bounds, Is.EqualTo(default(Rectangle)), "not an axis it scrolls");
        });
    }

    [Test]
    public void ABarNeverShowsOnAnAxisThatDoesNotScroll()
    {
        ScrollView view = Sized(new MeasuredBox(300, 100));
        view.ScrollBars = ScrollBarVisibility.Visible;
        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(HorizontalBar(view).Bounds, Is.EqualTo(default(Rectangle)), "the children overflow sideways, but that axis is not scrollable");
            Assert.That(VerticalBar(view).Bounds, Is.EqualTo(new Rectangle(94, 0, 6, 120)), "visible even without overflow");
        });
    }

    [Test]
    public void TwoBars_MeetAtAnEmptyCorner()
    {
        ScrollView view = Sized(new MeasuredBox(300, 500));
        view.Axes = ScrollAxes.Both;
        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(VerticalBar(view).Bounds, Is.EqualTo(new Rectangle(94, 0, 6, 114)));
            Assert.That(HorizontalBar(view).Bounds, Is.EqualTo(new Rectangle(0, 114, 94, 6)));
        });
    }

    [Test]
    public void TheThumb_IsTheViewportsShareAtTheOffsetsPosition()
    {
        ScrollView view = Sized(new MeasuredBox(80, 480));
        view.ScrollOffset = new Vector2Int(0, 180);
        Layout.Run(view, 100, 120);

        // 120 of 480 is a quarter of the 120-pixel track; 180 of the 360 range is half of the 90 pixels left.
        Assert.That(VerticalBar(view).ThumbBounds, Is.EqualTo(new Rectangle(94, 45, 6, 30)));
    }

    [Test]
    public void TheThumb_NeverShrinksBelowTheMinimum()
    {
        ScrollView view = Sized(new MeasuredBox(80, 100_000));
        view.ScrollOffset = new Vector2Int(0, int.MaxValue);
        Layout.Run(view, 100, 120);

        Assert.That(VerticalBar(view).ThumbBounds, Is.EqualTo(new Rectangle(94, 108, 6, 12)));
    }

    [Test]
    public void ATrackShorterThanTheMinimum_IsFilledByTheThumb()
    {
        ScrollView view = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(8), Children = { new MeasuredBox(20, 500) } };
        Layout.Run(view, 20, 8);

        Assert.That(VerticalBar(view).ThumbBounds, Is.EqualTo(new Rectangle(14, 0, 6, 8)));
    }

    [Test]
    public void AViewportThinnerThanTheBars_ClampsThem()
    {
        ScrollView view = new() { Axes = ScrollAxes.Both, Width = Sizing.Fixed(4), Height = Sizing.Fixed(4), Children = { new MeasuredBox(500, 500) } };
        Layout.Run(view, 4, 4);

        Assert.Multiple(() =>
        {
            Assert.That(VerticalBar(view).Bounds, Is.EqualTo(new Rectangle(0, 0, 4, 0)));
            Assert.That(HorizontalBar(view).Bounds, Is.EqualTo(new Rectangle(0, 0, 0, 4)));
            Assert.That(VerticalBar(view).ThumbBounds, Is.EqualTo(default(Rectangle)));
        });
    }

    [Test]
    public void WithNoChildren_NothingShows()
    {
        ScrollView view = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(120) };
        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(view.ScrollExtent, Is.EqualTo(default(Vector2Int)));
            Assert.That(view.MaxScrollOffset, Is.EqualTo(default(Vector2Int)));
            Assert.That(VerticalBar(view).Bounds, Is.EqualTo(default(Rectangle)));
        });
    }

    [Test]
    public void APressOnTheTrack_PagesTowardsIt()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        UiRoot root = Rooted(view);

        root.PointerPressed(new Vector2Int(97, 110), MouseButton.Left);
        root.PointerReleased(new Vector2Int(97, 110), MouseButton.Left);
        Vector2Int forward = view.ScrollOffset;
        root.Update();
        root.PointerPressed(new Vector2Int(97, 0), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(forward, Is.EqualTo(new Vector2Int(0, 120)));
            Assert.That(view.ScrollOffset, Is.EqualTo(default(Vector2Int)));
        });
    }

    [Test]
    public void APressOnTheThumb_IsTakenAndMovesNothing()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        UiRoot root = Rooted(view);

        bool consumed = root.PointerPressed(new Vector2Int(97, 5), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True);
            Assert.That(view.ScrollOffset, Is.EqualTo(default(Vector2Int)));
            Assert.That(VerticalBar(view).VisualState, Is.EqualTo(VisualState.Pressed));
        });
    }

    [Test]
    public void TheBar_TakesItsLookFromTheStyle()
    {
        SolidDrawable thumb = new(new Color(1, 2, 3, 255));
        SolidDrawable track = new(new Color(4, 5, 6, 255));
        ScrollView view = Sized(new MeasuredBox(80, 500));
        UiRoot root = new() { Style = new UiStyle { Scroll = new ScrollAppearance { Thumb = new StateDrawables(thumb), Track = track, Thickness = 10, MinimumThumbLength = 4 } } };
        root.AddLayer(new Column { Children = { view } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(VerticalBar(view).Bounds, Is.EqualTo(new Rectangle(90, 0, 10, 120)));
            Assert.That(root.Instructions.Any(instruction => instruction.Area == new Rectangle(90, 0, 10, 120) && instruction.Tint == (FColor)track.Color), Is.True, "the track");
            Assert.That(root.Instructions.Any(instruction => instruction.Area == new Rectangle(90, 0, 10, 28) && instruction.Tint == (FColor)thumb.Color), Is.True, "the thumb");
        });
    }

    // --- Adornments ----------------------------------------------------------------------

    [Test]
    public void TheBars_AreNotChildren()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));

        view.Children.Clear();
        Layout.Run(view, 100, 120);

        Assert.Multiple(() =>
        {
            Assert.That(view.Children, Is.Empty);
            Assert.That(view.Adornments, Has.Count.EqualTo(2));
            Assert.That(VerticalBar(view).Parent, Is.SameAs(view));
        });
    }

    [Test]
    public void TheBars_ArePaintedAfterTheChildren()
    {
        Element child = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(500), Background = new SolidDrawable(new Color(9, 9, 9, 255)) };
        ScrollView view = Sized(child);
        UiRoot root = Rooted(view);

        int childIndex = IndexOfInstruction(root, new Rectangle(0, 0, 100, 500));
        int thumbIndex = IndexOfInstruction(root, VerticalBar(view).ThumbBounds);

        Assert.That(thumbIndex, Is.GreaterThan(childIndex));
    }

    [Test]
    public void TheBars_FollowTheOwnersEnablementAndStyle()
    {
        ScrollView view = Sized(new MeasuredBox(80, 500));
        view.IsEnabled = false;
        UiRoot root = Rooted(view);

        VisualState disabled = VerticalBar(view).VisualState;
        root.Style = new UiStyle { Scroll = new ScrollAppearance { Thickness = 2 } };
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(disabled, Is.EqualTo(VisualState.Disabled));
            Assert.That(VerticalBar(view).Bounds.Width, Is.EqualTo(2), "a style change reaches the adornments");
        });
    }

    [Test]
    public void AnAdornment_CannotBeAttachedTwiceOrWhileParented()
    {
        Element owner = new();
        Element adornment = new();
        Element parented = new();
        _ = new Column { Children = { parented } };

        owner.AttachAdornment(adornment);

        Assert.Multiple(() =>
        {
            Assert.That(() => new Element().AttachAdornment(adornment), Throws.InvalidOperationException);
            Assert.That(() => owner.AttachAdornment(parented), Throws.InvalidOperationException);
            Assert.That(() => owner.AttachAdornment(null!), Throws.ArgumentNullException);
        });
    }

    // --- Helpers -------------------------------------------------------------------------

    private static ScrollView Sized(params Element[] children)
    {
        ScrollView view = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(120) };

        foreach (Element child in children)
        {
            view.Children.Add(child);
        }

        return view;
    }

    private static ScrollAxes Scroll(ScrollView view, Vector2 delta) => ((IScrollTarget)view).OnScroll(default, delta);

    private static ScrollBar VerticalBar(ScrollView view) => (ScrollBar)view.Adornments[1];

    private static ScrollBar HorizontalBar(ScrollView view) => (ScrollBar)view.Adornments[0];

    private static UiRoot Rooted(Element content)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { content } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }

    private static Rectangle HitArea(UiRoot root, Element element) => root.PointerTargetAreas[root.PointerTargetElements.IndexOf(element)];

    private static int IndexOfInstruction(UiRoot root, Rectangle area)
    {
        for (int i = 0; i < root.Instructions.Count; i++)
        {
            if (root.Instructions[i].Area == area)
            {
                return i;
            }
        }

        return -1;
    }
}
