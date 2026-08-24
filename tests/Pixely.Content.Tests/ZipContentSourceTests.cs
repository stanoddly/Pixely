using System.IO.Compression;
using Assert = NUnit.Framework.Assert;

namespace Pixely.Content.Tests;

public sealed class ZipContentSourceTests
{
    private DirectoryInfo _temporaryDirectory = null!;

    [SetUp]
    public void Setup()
    {
        _temporaryDirectory = Directory.CreateTempSubdirectory("Pixely.Content.Tests-");
    }

    [Test]
    public void GetDirectoriesFromLeafDirectoryReturnsEmpty()
    {
        using ZipContentSource contentSource = CreateContentSource(["sprites/terrain/ground.json"]);

        ReadOnlySpan<ContentFile> files = contentSource.GetFiles("sprites/terrain");
        ReadOnlySpan<string> directories = contentSource.GetDirectories("sprites/terrain");

        Assert.That(files.ToArray().Select(file => file.Path),
            Is.EquivalentTo(new[] { "sprites/terrain/ground.json" }));
        Assert.That(directories.Length, Is.Zero);
    }

    [Test]
    public void GetDirectoriesFromNestedDirectoryReturnsRootRelativePaths()
    {
        using ZipContentSource contentSource = CreateContentSource(["sprites/terrain/ground.json"]);

        ReadOnlySpan<string> directories = contentSource.GetDirectories("sprites");

        Assert.That(directories.ToArray(), Is.EquivalentTo(new[] { "sprites/terrain" }));
    }

    [Test]
    public void GetDirectoriesIncludesExplicitEmptyDirectories()
    {
        using ZipContentSource contentSource = CreateContentSource([], ["sprites/empty/"]);

        ReadOnlySpan<string> directories = contentSource.GetDirectories("sprites");
        ReadOnlySpan<ContentFile> files = contentSource.GetFiles("sprites/empty");
        ReadOnlySpan<string> emptyDirectoryChildren = contentSource.GetDirectories("sprites/empty");

        Assert.That(directories.ToArray(), Is.EquivalentTo(new[] { "sprites/empty" }));
        Assert.That(files.Length, Is.Zero);
        Assert.That(emptyDirectoryChildren.Length, Is.Zero);
    }

    [TearDown]
    public void Teardown()
    {
        _temporaryDirectory.Delete(true);
    }

    private ZipContentSource CreateContentSource(string[] filePaths, string[]? directoryPaths = null)
    {
        string archivePath = Path.Combine(_temporaryDirectory.FullName, "Content.pk3");

        using (ZipArchive archive = System.IO.Compression.ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            foreach (string directoryPath in directoryPaths ?? [])
            {
                archive.CreateEntry(directoryPath);
            }

            foreach (string filePath in filePaths)
            {
                archive.CreateEntry(filePath);
            }
        }

        return ZipContentSource.Create(archivePath);
    }
}
