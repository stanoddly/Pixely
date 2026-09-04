namespace Pixely.Ui.Tests;

public class ElementTests
{
    [Test]
    public void Padding_DeflatesTheContentBox()
    {
        MeasuredBox child = new(20, 20);
        Column root = new() { Padding = new Thickness(5, 10, 15, 20), Children = { child } };

        Vector2Int size = Layout.MeasureUnbounded(root);
        root.Arrange(new Rectangle(0, 0, size.X, size.Y), new Rectangle(0, 0, size.X, size.Y));

        Assert.Multiple(() =>
        {
            Assert.That(size, Is.EqualTo(new Vector2Int(40, 50)));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(5, 10, 20, 20)));
        });
    }

    [Test]
    public void Margin_CountsTowardsTheParentButNotTheChildBounds()
    {
        MeasuredBox child = new(20, 20) { Margin = new Thickness(4) };
        Column root = new() { Children = { child } };

        Vector2Int size = Layout.MeasureUnbounded(root);
        root.Arrange(new Rectangle(0, 0, size.X, size.Y), new Rectangle(0, 0, size.X, size.Y));

        Assert.Multiple(() =>
        {
            Assert.That(size, Is.EqualTo(new Vector2Int(28, 28)));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(4, 4, 20, 20)));
        });
    }

    [Test]
    public void NegativeMargin_ShrinksTheReportedSize()
    {
        MeasuredBox child = new(20, 20) { Margin = new Thickness(-4, 0, -4, 0) };
        Column root = new() { Children = { child } };

        Vector2Int size = Layout.MeasureUnbounded(root);

        Assert.That(size.X, Is.EqualTo(12));
    }

    [Test]
    public void NegativePadding_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Column().Padding = new Thickness(-1, 0, 0, 0));
    }

    [Test]
    public void NegativeGap_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StackLayout(Orientation.Vertical, -1));
    }

    [Test]
    public void Alignment_PositionsWithinTheOfferedSlot()
    {
        MeasuredBox start = new(20, 10);
        MeasuredBox center = new(20, 10) { HorizontalAlignment = Alignment.Center };
        MeasuredBox end = new(20, 10) { HorizontalAlignment = Alignment.End };
        Column root = new() { Children = { start, center, end } };

        Layout.Run(root, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(start.Bounds.X, Is.EqualTo(0));
            Assert.That(center.Bounds.X, Is.EqualTo(40));
            Assert.That(end.Bounds.X, Is.EqualTo(80));
        });
    }

    [Test]
    public void InvisibleChild_TakesNoSpaceAndIsSkipped()
    {
        MeasuredBox visible = new(20, 20);
        MeasuredBox hidden = new(50, 50) { IsVisible = false };
        Column root = new(gap: 10) { Children = { visible, hidden } };

        Vector2Int size = Layout.MeasureUnbounded(root);

        Assert.Multiple(() =>
        {
            Assert.That(size, Is.EqualTo(new Vector2Int(20, 20)), "a hidden child adds neither size nor gap");
            Assert.That(hidden.MeasureCount, Is.Zero);
        });
    }

    [Test]
    public void Measure_WithUnchangedConstraints_UsesTheCache()
    {
        MeasuredBox child = new(20, 20);
        Column root = new() { Children = { child } };

        Layout.Measure(root, 100, 100);
        Layout.Measure(root, 100, 100);

        Assert.That(child.MeasureCount, Is.EqualTo(1));
    }

    [Test]
    public void Measure_WithDifferentConstraints_MeasuresAgain()
    {
        MeasuredBox child = new(20, 20);
        Column root = new() { Children = { child } };

        Layout.Measure(root, 100, 100);
        Layout.Measure(root, 120, 100);

        Assert.That(child.MeasureCount, Is.EqualTo(2));
    }

    [Test]
    public void Measure_DistinguishesDefiniteFromIndefiniteConstraintsWithTheSameMaximum()
    {
        MeasuredBox child = new(20, 20) { Width = Sizing.Grow() };
        Column root = new() { Children = { child } };

        Layout.Measure(root, Constraints.Loose(100, 100));
        Vector2Int definite = Layout.Measure(root, Constraints.Tight(new Vector2Int(100, 100)));

        // A loose 0..100 and a tight 100..100 share a maximum but not a meaning, so they must not
        // share a cache entry: Grow degrades under the first and fills under the second.
        Assert.That(definite.X, Is.EqualTo(100));
        Assert.That(child.DesiredSize.X, Is.EqualTo(100));
    }

    [Test]
    public void ChangingAProperty_InvalidatesMeasureUpToTheRoot()
    {
        MeasuredBox child = new(20, 20);
        Column inner = new() { Children = { child } };
        Column root = new() { Children = { inner } };

        Layout.Measure(root, 100, 100);
        child.IntrinsicSize = new Vector2Int(30, 30);

        Assert.Multiple(() =>
        {
            Assert.That(child.IsMeasureDirty, Is.True);
            Assert.That(inner.IsMeasureDirty, Is.True);
            Assert.That(root.IsMeasureDirty, Is.True);
        });
    }

    [Test]
    public void SettingAPropertyToItsCurrentValue_DoesNotInvalidate()
    {
        MeasuredBox child = new(20, 20);
        Column root = new() { Children = { child } };

        Layout.Measure(root, 100, 100);
        child.IntrinsicSize = new Vector2Int(20, 20);

        Assert.That(root.IsMeasureDirty, Is.False);
    }

    [Test]
    public void ArrangeOnlyProperty_DoesNotInvalidateMeasure()
    {
        MeasuredBox child = new(20, 20);
        Column root = new() { Children = { child } };

        Layout.Run(root, 100, 100);
        child.ClipsContent = true;

        Assert.Multiple(() =>
        {
            Assert.That(root.IsMeasureDirty, Is.False);
            Assert.That(root.IsArrangeDirty, Is.True);
        });
    }

    [Test]
    public void PaintOnlyProperty_DoesNotInvalidateArrange()
    {
        MeasuredBox child = new(20, 20);
        Column root = new() { Children = { child } };

        Layout.Run(root, 100, 100);
        child.IsEnabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(root.IsArrangeDirty, Is.False);
            Assert.That(root.IsPaintDirty, Is.True);
        });
    }

    [Test]
    public void AddingAChild_InvalidatesTheParent()
    {
        Column root = new();
        Layout.Measure(root, 100, 100);

        root.Children.Add(new MeasuredBox(10, 10));

        Assert.That(root.IsMeasureDirty, Is.True);
    }

    [Test]
    public void AddingAnElementThatAlreadyHasAParent_Throws()
    {
        MeasuredBox child = new(10, 10);
        Column first = new() { Children = { child } };
        Column second = new();

        Assert.Throws<InvalidOperationException>(() => second.Children.Add(child));
        Assert.That(first.Children, Has.Count.EqualTo(1));
    }

    [Test]
    public void RemovingAChild_ClearsItsParent()
    {
        MeasuredBox child = new(10, 10);
        Column root = new() { Children = { child } };

        Assert.That(root.Children.Remove(child), Is.True);
        Assert.That(child.Parent, Is.Null);
    }

    [Test]
    public void IsEffectivelyEnabled_FollowsAncestors()
    {
        MeasuredBox child = new(10, 10);
        Column inner = new() { Children = { child } };
        Column root = new() { Children = { inner } };

        Assert.That(child.IsEffectivelyEnabled, Is.True);

        root.IsEnabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(child.IsEnabled, Is.True, "local state is unchanged");
            Assert.That(child.IsEffectivelyEnabled, Is.False);
        });
    }
}
