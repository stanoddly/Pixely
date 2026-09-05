namespace Pixely.Ui.Tests;

/// <summary>
/// Elements are constructed with <c>new</c>, so they cannot be given shared state by dependency
/// injection. They reach it through the root instead, which means the link to the root has to stay
/// correct as the tree is edited.
/// </summary>
public class OwnerRootTests
{
    [Test]
    public void DetachedElement_HasNoRoot()
    {
        MeasuredBox box = new(10, 10);

        Assert.That(box.OwnerRoot, Is.Null);
    }

    [Test]
    public void AddingALayer_RootsItsWholeSubtree()
    {
        MeasuredBox leaf = new(10, 10);
        Column inner = new() { Children = { leaf } };
        Column layer = new() { Children = { inner } };
        UiRoot root = new();

        root.AddLayer(layer);

        Assert.Multiple(() =>
        {
            Assert.That(layer.OwnerRoot, Is.SameAs(root));
            Assert.That(inner.OwnerRoot, Is.SameAs(root));
            Assert.That(leaf.OwnerRoot, Is.SameAs(root));
        });
    }

    [Test]
    public void AddingToARootedParent_RootsTheNewSubtree()
    {
        Column layer = new();
        UiRoot root = new();
        root.AddLayer(layer);

        MeasuredBox leaf = new(10, 10);
        Column branch = new() { Children = { leaf } };
        layer.Children.Add(branch);

        Assert.Multiple(() =>
        {
            Assert.That(branch.OwnerRoot, Is.SameAs(root));
            Assert.That(leaf.OwnerRoot, Is.SameAs(root), "the link reaches children added before the parent was rooted");
        });
    }

    [Test]
    public void RemovingASubtree_ClearsItsRoot()
    {
        MeasuredBox leaf = new(10, 10);
        Column branch = new() { Children = { leaf } };
        Column layer = new() { Children = { branch } };
        UiRoot root = new();
        root.AddLayer(layer);

        layer.Children.Remove(branch);

        Assert.Multiple(() =>
        {
            Assert.That(branch.OwnerRoot, Is.Null);
            Assert.That(leaf.OwnerRoot, Is.Null);
            Assert.That(layer.OwnerRoot, Is.SameAs(root));
        });
    }

    [Test]
    public void RemovingALayer_ClearsItsRoot()
    {
        MeasuredBox leaf = new(10, 10);
        Column layer = new() { Children = { leaf } };
        UiRoot root = new();
        root.AddLayer(layer);

        root.RemoveLayer(layer);

        Assert.Multiple(() =>
        {
            Assert.That(layer.OwnerRoot, Is.Null);
            Assert.That(leaf.OwnerRoot, Is.Null);
        });
    }

    [Test]
    public void MovingASubtreeToAnotherRoot_ReportsTheNewRoot()
    {
        MeasuredBox leaf = new(10, 10);
        Column branch = new() { Children = { leaf } };
        Column firstLayer = new() { Children = { branch } };
        Column secondLayer = new();
        UiRoot firstRoot = new();
        UiRoot secondRoot = new();
        firstRoot.AddLayer(firstLayer);
        secondRoot.AddLayer(secondLayer);

        firstLayer.Children.Remove(branch);
        secondLayer.Children.Add(branch);

        Assert.Multiple(() =>
        {
            Assert.That(branch.OwnerRoot, Is.SameAs(secondRoot));
            Assert.That(leaf.OwnerRoot, Is.SameAs(secondRoot));
        });
    }

    [Test]
    public void ClearingChildren_ClearsTheirRoots()
    {
        MeasuredBox leaf = new(10, 10);
        Column layer = new() { Children = { leaf } };
        UiRoot root = new();
        root.AddLayer(layer);

        layer.Children.Clear();

        Assert.That(leaf.OwnerRoot, Is.Null);
    }
}
