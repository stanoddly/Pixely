using System.Diagnostics.CodeAnalysis;
using Pixely.App;
using Pixely.Content;
using Pixely.DependencyInjection;

namespace Pixely.Tests;

public sealed class ContentServiceCollectionExtensionsTests
{
    [Test]
    public void PixelyAppBuilder_WithoutSources_RegistersEmptyFileSystem()
    {
        PixelyAppBuilder builder = new();
        using ServiceProvider provider = builder.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<VirtualFileSystem>(), Is.SameAs(DictFileSystem.Empty));
    }

    [Test]
    public void AddFileSystem_WithSingleSource_RegistersSourceDirectly()
    {
        PixelyAppBuilder services = new();
        TestFileSystem source = new("source");
        services.AddFileSystem(source);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<VirtualFileSystem>(), Is.SameAs(source));
    }

    [Test]
    public void AddFileSystem_WithMultipleSources_PreservesLastSourcePrecedence()
    {
        PixelyAppBuilder services = new();
        services.AddFileSystem(new TestFileSystem("first"));
        services.AddFileSystem(new TestFileSystem("last"));
        using ServiceProvider provider = services.BuildServiceProvider();
        VirtualFileSystem fileSystem = provider.GetRequiredService<VirtualFileSystem>();
        using StreamReader reader = new(fileSystem.OpenStream("marker.txt"));

        Assert.That(reader.ReadToEnd(), Is.EqualTo("last"));
    }

    [Test]
    public void AddFileSystemCache_WrapsConfiguredSources()
    {
        PixelyAppBuilder services = new();
        services.AddFileSystem(new TestFileSystem("source"));
        services.AddFileSystemCache();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<VirtualFileSystem>(), Is.TypeOf<CachedFileSystem>());
    }

    [Test]
    public void ProviderDisposal_DisposesSourceOnce()
    {
        PixelyAppBuilder services = new();
        TestFileSystem source = new("source");
        services.AddFileSystem(source);

        using (ServiceProvider provider = services.BuildServiceProvider())
        {
            Assert.That(provider.GetRequiredService<VirtualFileSystem>(), Is.SameAs(source));
        }

        Assert.That(source.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void ChildServiceCollection_ContentConfiguration_Throws()
    {
        PixelyAppBuilder rootServices = new();
        rootServices.AddFileSystem(new TestFileSystem("root"));
        using ServiceProvider rootProvider = rootServices.BuildServiceProvider();
        ServiceCollection childServices = rootProvider.CreateServiceCollection();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => childServices.AddFileSystem(new TestFileSystem("child")))!;

        Assert.That(exception.Message, Is.EqualTo("Content can only be configured on a root service collection."));
    }

    private sealed class TestFileSystem : VirtualFileSystem
    {
        private readonly ByteVirtualFile _file;

        public int DisposeCount { get; private set; }

        public TestFileSystem(string content)
        {
            _file = new ByteVirtualFile("marker.txt", System.Text.Encoding.UTF8.GetBytes(content));
        }

        public override bool TryGetFiles(ReadOnlySpan<char> path, out ReadOnlySpan<VirtualFile> result)
        {
            result = path.SequenceEqual(".") ? new VirtualFile[] { _file } : Array.Empty<VirtualFile>();
            return path.SequenceEqual(".");
        }

        public override bool TryGetDirectories(ReadOnlySpan<char> path, out ReadOnlySpan<string> result)
        {
            result = Array.Empty<string>();
            return path.SequenceEqual(".");
        }

        public override bool TryGetFile(ReadOnlySpan<char> path, [NotNullWhen(true)] out VirtualFile? file)
        {
            if (path.SequenceEqual(_file.Path))
            {
                file = _file;
                return true;
            }

            file = null;
            return false;
        }

        public override void Dispose()
        {
            DisposeCount++;
        }
    }
}
