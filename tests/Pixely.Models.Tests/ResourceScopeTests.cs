using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Models.Tests;

public class ResourceScopeTests
{
    [Test]
    public void Dispose_WithoutRelease_DisposesInReverseOrder()
    {
        List<string> disposed = new();
        ResourceScope scope = new();
        scope.Add(new Resource("first", disposed));
        scope.Add(new Resource("second", disposed));

        scope.Dispose();

        Assert.That(disposed, Is.EqualTo(new[] { "second", "first" }));
    }

    [Test]
    public void Dispose_AfterRelease_DisposesNothing()
    {
        List<string> disposed = new();
        ResourceScope scope = new();
        scope.Add(new Resource("first", disposed));

        ImmutableArray<IDisposable> released = scope.Release();
        scope.Dispose();

        Assert.That(disposed, Is.Empty);
        Assert.That(released, Has.Length.EqualTo(1));
    }

    // The loader creates resources inside a scope, so an exception thrown partway, such as by an image decoder, releases what
    // was created before it.
    [Test]
    public void Using_WhenTheLoadThrows_DisposesWhatWasCreated()
    {
        List<string> disposed = new();

        Assert.Throws<InvalidDataException>(() =>
        {
            using ResourceScope scope = new();
            scope.Add(new Resource("vertex buffer", disposed));
            throw new InvalidDataException("decoder failed");
        });

        Assert.That(disposed, Is.EqualTo(new[] { "vertex buffer" }));
    }

    [Test]
    public void ModelDispose_DisposesItsResourcesOnce()
    {
        List<string> disposed = new();
        Model model = new([], [], [], null, [], [], [], [], [new Resource("texture", disposed), new Resource("index buffer", disposed)]);

        model.Dispose();
        model.Dispose();

        Assert.That(disposed, Is.EqualTo(new[] { "index buffer", "texture" }));
    }

    [Test]
    public void ModelPose_OfModel_UsesItsNodes()
    {
        ModelNode node = new(null, null, [], new NodeTransform(Vector3.UnitX, Quaternion.Identity, Vector3.One), null, null);
        Model model = new([node], [0], [], null, [], [], [], [], []);

        Assert.That(new ModelPose(model).LocalTransforms[0].Translation, Is.EqualTo(Vector3.UnitX));
    }

    private sealed class Resource(string name, List<string> disposed) : IDisposable
    {
        public void Dispose() => disposed.Add(name);
    }
}
