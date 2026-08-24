namespace Pixely.Content.Tests;

public class CachedContentSourceTests : BaseContentSourceTests
{
    [SetUp]
    public void Setup()
    {
        Source = CachedContentSource.Create(new DirectoryContentSource("Content"));
    }
}
