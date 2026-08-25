using Assert = NUnit.Framework.Assert;

namespace Pixely.Content.Tests;

public class CompositeContentSourceTests : BaseContentSourceTests
{
    [SetUp]
    public void Setup()
    {
        Source = new CompositeContentSource([new DirectoryContentSource("ContentPart1"), new DirectoryContentSource("ContentPart2")]);
    }

    [Test]
    public void GetFilesSucceedsWhenEarlierSourceLacksDirectory()
    {
        ReadOnlySpan<ContentFile> files = Source.GetFiles("dir2");

        string[] expected = ["dir2/dir2a.txt", "dir2/dir2b.txt"];
        ContentFile[] items = files.ToArray();
        Assert.That(items.Select(x => x.Path), Is.EquivalentTo(expected));
    }
}
