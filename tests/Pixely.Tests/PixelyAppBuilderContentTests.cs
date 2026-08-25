using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Pixely.App;
using Pixely.Content;
using Pixely.DependencyInjection;

namespace Pixely.Tests;

public sealed class PixelyAppBuilderContentTests
{
    [Test]
    public void WithoutSources_RegistersEmptyContentSource()
    {
        PixelyAppBuilder appBuilder = new();
        using ServiceProvider provider = appBuilder.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ContentSource>(), Is.SameAs(DictionaryContentSource.Empty));
    }

    [Test]
    public void ConfigureContent_WithSingleSource_RegistersSourceDirectly()
    {
        PixelyAppBuilder appBuilder = new();
        TestContentSource source = new("source");
        appBuilder.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddSource(source));
        using ServiceProvider provider = appBuilder.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ContentSource>(), Is.SameAs(source));
    }

    [Test]
    public void ConfigureContent_WithMultipleCalls_PreservesLastSourcePrecedence()
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddSource(new TestContentSource("first")));
        appBuilder.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddSource(new TestContentSource("last")));
        using ServiceProvider provider = appBuilder.BuildServiceProvider();
        ContentSource contentSource = provider.GetRequiredService<ContentSource>();
        using StreamReader reader = new(contentSource.OpenStream("marker.txt"));

        Assert.That(reader.ReadToEnd(), Is.EqualTo("last"));
    }

    [Test]
    public void ConfigureContent_WithCache_WrapsConfiguredSources()
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddSource(new TestContentSource("source")).WithCache());
        using ServiceProvider provider = appBuilder.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ContentSource>(), Is.TypeOf<CachedContentSource>());
    }

    [Test]
    public void ProviderDisposal_DisposesSourceOnce()
    {
        PixelyAppBuilder appBuilder = new();
        TestContentSource source = new("source");
        appBuilder.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddSource(source));

        using (ServiceProvider provider = appBuilder.BuildServiceProvider())
        {
            Assert.That(provider.GetRequiredService<ContentSource>(), Is.SameAs(source));
        }

        Assert.That(source.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void UseDefaultContent_RegistersConventionSource()
    {
        string contentName = $"content-{Guid.NewGuid():N}";
        string archivePath = Path.Combine(AppContext.BaseDirectory, $"{contentName}.pk3");

        try
        {
            using (ZipArchive archive = System.IO.Compression.ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("marker.txt");
                using StreamWriter writer = new(entry.Open());
                writer.Write("default");
            }

            PixelyAppBuilder appBuilder = new();
            appBuilder.UseDefaultContent(contentName);
            using ServiceProvider provider = appBuilder.BuildServiceProvider();
            using StreamReader reader = new(provider.GetRequiredService<ContentSource>().OpenStream("marker.txt"));

            Assert.That(reader.ReadToEnd(), Is.EqualTo("default"));
        }
        finally
        {
            File.Delete(archivePath);
        }
    }

    private sealed class TestContentSource : ContentSource
    {
        private readonly ByteContentFile _file;

        public int DisposeCount { get; private set; }

        public TestContentSource(string content)
        {
            _file = new ByteContentFile("marker.txt", System.Text.Encoding.UTF8.GetBytes(content));
        }

        public override bool TryGetFiles(ReadOnlySpan<char> path, out ReadOnlySpan<ContentFile> result)
        {
            result = path.SequenceEqual(".") ? new ContentFile[] { _file } : Array.Empty<ContentFile>();
            return path.SequenceEqual(".");
        }

        public override bool TryGetDirectories(ReadOnlySpan<char> path, out ReadOnlySpan<string> result)
        {
            result = Array.Empty<string>();
            return path.SequenceEqual(".");
        }

        public override bool TryGetFile(ReadOnlySpan<char> path, [NotNullWhen(true)] out ContentFile? file)
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
