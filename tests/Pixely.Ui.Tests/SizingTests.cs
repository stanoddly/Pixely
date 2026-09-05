namespace Pixely.Ui.Tests;

/// <summary>
/// The sizing rules, one test per row of the specification. Definiteness is the load-bearing
/// concept: Grow and Percent need an extent the parent has committed to, and degrade to Fit
/// without one.
/// </summary>
public class SizingTests
{
    [Test]
    public void Fixed_IsHonouredExactly_EvenWhenLargerThanTheSpaceOffered()
    {
        MeasuredBox child = new(10, 10) { Width = Sizing.Fixed(400) };
        Column root = new() { Children = { child } };

        Layout.Run(root, 100, 100);

        Assert.That(child.Bounds.Width, Is.EqualTo(400));
    }

    [Test]
    public void Percent_ResolvesAgainstTheParentContentBox()
    {
        MeasuredBox child = new() { Width = Sizing.Percent(0.5f) };
        Column root = new() { Padding = new Thickness(10), Children = { child } };

        Layout.Run(root, 200, 100);

        // 200 wide minus 10 padding either side leaves a 180 content box.
        Assert.That(child.Bounds.Width, Is.EqualTo(90));
    }

    [Test]
    public void Percent_TwoHalfSiblings_EachGetHalfOfTheHostNotOfTheRemainder()
    {
        MeasuredBox first = new() { Width = Sizing.Percent(0.5f) };
        MeasuredBox second = new() { Width = Sizing.Percent(0.5f) };
        Row root = new() { Children = { first, second } };

        Layout.Run(root, 200, 100);

        Assert.Multiple(() =>
        {
            Assert.That(first.Bounds.Width, Is.EqualTo(100));
            Assert.That(second.Bounds.Width, Is.EqualTo(100));
        });
    }

    [Test]
    public void Percent_OnAnIndefiniteAxis_DegradesToFit()
    {
        MeasuredBox child = new(30, 10) { Width = Sizing.Percent(0.5f) };
        Column root = new() { Children = { child } };

        Layout.Measure(root, Constraints.Unconstrained);

        Assert.That(child.DesiredSize.X, Is.EqualTo(30));
    }

    [Test]
    public void Grow_OnAnIndefiniteAxis_DegradesToFit()
    {
        MeasuredBox child = new(30, 20) { Height = Sizing.Grow() };
        Column root = new() { Children = { child } };

        Vector2Int size = Layout.Measure(root, Constraints.Unconstrained);

        Assert.Multiple(() =>
        {
            Assert.That(child.DesiredSize.Y, Is.EqualTo(20));
            Assert.That(size.Y, Is.EqualTo(20));
        });
    }

    [Test]
    public void Grow_SharesTheRemainderAfterFitSiblings()
    {
        MeasuredBox fixedChild = new(10, 40);
        MeasuredBox growing = new() { Height = Sizing.Grow() };
        Column root = new() { Children = { fixedChild, growing } };

        Layout.Run(root, 100, 200);

        Assert.That(growing.Bounds.Height, Is.EqualTo(160));
    }

    [Test]
    public void Grow_SplitsByWeight()
    {
        MeasuredBox one = new() { Height = Sizing.Grow(1f) };
        MeasuredBox three = new() { Height = Sizing.Grow(3f) };
        Column root = new() { Children = { one, three } };

        Layout.Run(root, 100, 200);

        Assert.Multiple(() =>
        {
            Assert.That(one.Bounds.Height, Is.EqualTo(50));
            Assert.That(three.Bounds.Height, Is.EqualTo(150));
        });
    }

    [Test]
    public void Grow_AccountsForGaps()
    {
        MeasuredBox first = new() { Height = Sizing.Grow() };
        MeasuredBox second = new() { Height = Sizing.Grow() };
        Column root = new(gap: 10) { Children = { first, second } };

        Layout.Run(root, 100, 210);

        Assert.Multiple(() =>
        {
            Assert.That(first.Bounds.Height, Is.EqualTo(100));
            Assert.That(second.Bounds.Height, Is.EqualTo(100));
            Assert.That(second.Bounds.Y, Is.EqualTo(110));
        });
    }

    [Test]
    public void Grow_DistributesLeftoverPixelsExactly()
    {
        MeasuredBox first = new() { Height = Sizing.Grow() };
        MeasuredBox second = new() { Height = Sizing.Grow() };
        MeasuredBox third = new() { Height = Sizing.Grow() };
        Column root = new() { Children = { first, second, third } };

        Layout.Run(root, 100, 100);

        int total = first.Bounds.Height + second.Bounds.Height + third.Bounds.Height;
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(100), "grow allocations must total the surplus exactly");
            Assert.That(first.Bounds.Height, Is.EqualTo(34));
            Assert.That(second.Bounds.Height, Is.EqualTo(33));
            Assert.That(third.Bounds.Height, Is.EqualTo(33));
        });
    }

    [Test]
    public void Grow_OnTheCrossAxis_FillsWhenTheCrossAxisIsDefinite()
    {
        MeasuredBox child = new(10, 10) { Width = Sizing.Grow() };
        Column root = new() { Children = { child } };

        Layout.Run(root, 200, 100);

        Assert.That(child.Bounds.Width, Is.EqualTo(200));
    }

    [Test]
    public void Grow_OnTheCrossAxis_DegradesToFitWhenTheCrossAxisIsIndefinite()
    {
        MeasuredBox child = new(10, 10) { Width = Sizing.Grow() };
        Column root = new() { Children = { child } };

        Layout.Measure(root, Constraints.Unconstrained);

        Assert.That(child.DesiredSize.X, Is.EqualTo(10));
    }

    [Test]
    public void Stretch_FillsTheOfferedExtent()
    {
        MeasuredBox child = new(10, 10) { HorizontalAlignment = Alignment.Stretch };
        Column root = new() { Children = { child } };

        Layout.Run(root, 200, 100);

        Assert.That(child.Bounds.Width, Is.EqualTo(200));
    }

    [Test]
    public void Stretch_LosesToAnExplicitSize()
    {
        MeasuredBox child = new(10, 10)
        {
            Width = Sizing.Fixed(40),
            HorizontalAlignment = Alignment.Stretch
        };
        Column root = new() { Children = { child } };

        Layout.Run(root, 200, 100);

        Assert.Multiple(() =>
        {
            Assert.That(child.Bounds.Width, Is.EqualTo(40));
            Assert.That(child.Bounds.X, Is.EqualTo(0), "stretch falls back to Start when a size is explicit");
        });
    }

    [Test]
    public void Stretch_DegradesWhenTheParentAxisIsIndefinite()
    {
        MeasuredBox child = new(10, 10) { HorizontalAlignment = Alignment.Stretch };
        Column root = new() { Children = { child } };

        Layout.Measure(root, Constraints.Unconstrained);

        Assert.That(child.DesiredSize.X, Is.EqualTo(10));
    }

    [Test]
    public void Sizing_RejectsInvalidFactors()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Sizing.Fixed(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sizing.Grow(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sizing.Grow(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sizing.Percent(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Sizing.Percent(1.5f));
        });
    }

    [Test]
    public void Constraints_ClampAppliesMinimumOverTheZeroClamp()
    {
        Constraints constraints = new(50, 50, 100, 100);

        Assert.That(constraints.Clamp(new Vector2Int(-10, 10)), Is.EqualTo(new Vector2Int(50, 50)));
    }

    [Test]
    public void Measure_SaturatesInsteadOfOverflowing()
    {
        MeasuredBox child = new(int.MaxValue, 10);
        Column root = new() { Padding = new Thickness(10), Children = { child } };

        Vector2Int size = Layout.Measure(root, Constraints.Unconstrained);

        Assert.That(size.X, Is.EqualTo(int.MaxValue));
    }
}
