using Assert = NUnit.Framework.Assert;

namespace Pixely.Content.Tests;

public abstract class BaseContentSourceTests
{
    // this is supposed to be assigned in a derived class
    protected ContentSource Source { get; set; } = DictionaryContentSource.Empty;

    [Test]
    public void GetFilesFromRootSucceeds()
    {
        // act
        ReadOnlySpan<ContentFile> files = Source.GetFiles(".");

        // assert
        string[] expected = ["a.txt", "b.txt"];
        ContentFile[] items = files.ToArray();
        Assert.That(items.Select(x => x.Path), Is.EquivalentTo(expected));
    }

    [Test]
    public void GetDirectoriesFromRootSucceeds()
    {
        // act
        ReadOnlySpan<string> dirs = Source.GetDirectories(".");

        // assert
        string[] expected = ["dir1", "dir2"];
        string[] items = dirs.ToArray();
        Assert.That(items, Is.EquivalentTo(expected));
    }

    [Test]
    public void GetFilesFromSubdirectorySucceeds()
    {
        // act
        ReadOnlySpan<ContentFile> files = Source.GetFiles("dir1");

        // assert
        string[] expected = ["dir1/dir1a.txt", "dir1/dir1b.txt"];
        ContentFile[] items = files.ToArray();
        Assert.That(items.Select(x => x.Path), Is.EquivalentTo(expected));
    }

    [Test]
    public void TryGetFilesFromSubdirectorySucceeds()
    {
        bool found = Source.TryGetFiles("dir1", out ReadOnlySpan<ContentFile> files);

        string[] expected = ["dir1/dir1a.txt", "dir1/dir1b.txt"];
        ContentFile[] items = files.ToArray();
        Assert.That(found, Is.True);
        Assert.That(items.Select(x => x.Path), Is.EquivalentTo(expected));
    }

    [Test]
    public void GetFilesFromNonexistentDirectoryThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(() => Source.GetFiles("nonexistent"));
    }

    [Test]
    public void TryGetFilesFromNonexistentDirectoryReturnsFalse()
    {
        bool found = Source.TryGetFiles("nonexistent", out ReadOnlySpan<ContentFile> files);

        Assert.That(found, Is.False);
        Assert.That(files.Length, Is.EqualTo(0));
    }

    [Test]
    public void GetDirectoriesFromNonexistentDirectoryThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(() => Source.GetDirectories("nonexistent"));
    }

    [Test]
    public void OpenStreamFromFileSucceeds()
    {
        // act
        using Stream stream = Source.OpenStream("a.txt");
        using StreamReader reader = new StreamReader(stream);
        string fileContents = reader.ReadToEnd();

        // assert
        Assert.That(fileContents, Is.EqualTo("Hello a"));
    }

    [Test]
    public void OpenStreamFromNonexistentThrowsException()
    {
        // act & assert
        Assert.Throws<FileNotFoundException>(() => Source.OpenStream("nonexistent"));
    }

    [TearDown]
    public void Teardown()
    {
        Source.Dispose();
    }
}
