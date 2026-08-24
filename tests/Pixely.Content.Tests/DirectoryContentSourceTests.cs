namespace Pixely.Content.Tests;

public class DirectoryContentSourceTests : BaseContentSourceTests
{
    [SetUp]
    public void Setup()
    {
        Source = new DirectoryContentSource("Content");
    }
}
