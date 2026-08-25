using System.IO.Compression;

namespace Pixely.Content.Tests;

public class ContentSourceBuilderTests
{
    [Test]
    public void CreateReturnsDirectoryContentSourceDirectly()
    {
        // arrange
        ContentSource contentSource = new ContentSourceBuilder()
            .AddDirectory("Content")
            .Create();

        // assert
        Assert.That(contentSource is DirectoryContentSource);
    }

    [Test]
    public void CreateReturnsCompositeContentSource()
    {
        // arrange
        ContentSource contentSource = new ContentSourceBuilder()
            .AddDirectory("ContentPart1")
            .AddDirectory("ContentPart2")
            .Create();

        // assert
        Assert.That(contentSource is CompositeContentSource);
    }

    [Test]
    public void CreateReturnsCachedContentSource()
    {
        // arrange
        ContentSource contentSource = new ContentSourceBuilder()
            .AddDirectory("Content")
            .WithCache()
            .Create();

        // assert
        Assert.That(contentSource is CachedContentSource);
    }

    [Test]
    public void CreateReturnsDirectoryContentSourceFromProjectsDirectory()
    {
        // arrange
        ContentSource contentSource = new ContentSourceBuilder()
            .AddProjectDirectory("ContentInDevRoot")
            .Create();

        // assert
        Assert.That(contentSource is DirectoryContentSource);
        DirectoryContentSource directoryContentSource = (DirectoryContentSource)contentSource;
        string expectedPath = Path.Join(typeof(ContentSourceBuilderTests).Namespace, "ContentInDevRoot");
        Assert.That(directoryContentSource.RootPath.EndsWith(expectedPath));
    }

    [Test]
    public void AddProjectDirectoryPrefersAppBaseDirectoryWhenContentExists()
    {
        // arrange
        string expectedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Content"));
        // This test exercises the app-base-directory branch, which only applies when the
        // directory actually exists next to the test assembly (copied at build time).
        Assert.That(Directory.Exists(expectedPath), $"Expected '{expectedPath}' to exist next to the test assembly.");
        ContentSource contentSource = new ContentSourceBuilder()
            .AddProjectDirectory("Content")
            .Create();

        // assert
        Assert.That(contentSource is DirectoryContentSource);
        DirectoryContentSource directoryContentSource = (DirectoryContentSource)contentSource;
        Assert.That(directoryContentSource.RootPath, Is.EqualTo(expectedPath));
    }

    [Test]
    // Directory.SetCurrentDirectory affects the entire test process, so this test cannot run in parallel.
    [NonParallelizable]
    public void AddZipPatternResolvesPatternRelativeToAppBaseDirectory()
    {
        string originalWorkingDirectory = Directory.GetCurrentDirectory();
        string archiveFilename = $"content-{Guid.NewGuid():N}.pk3";
        string archivePath = Path.Combine(AppContext.BaseDirectory, archiveFilename);
        DirectoryInfo temporaryWorkingDirectory = Directory.CreateTempSubdirectory("Pixely.Content.Tests-");

        try
        {
            using (ZipArchive archive = System.IO.Compression.ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("marker.txt");
                using StreamWriter writer = new(entry.Open());
                writer.Write("from app directory");
            }

            Directory.SetCurrentDirectory(temporaryWorkingDirectory.FullName);

            using ContentSource contentSource = new ContentSourceBuilder()
                .AddZipPattern(archiveFilename)
                .Create();
            using StreamReader reader = new(contentSource.OpenStream("marker.txt"));

            Assert.That(reader.ReadToEnd(), Is.EqualTo("from app directory"));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            File.Delete(archivePath);
            if (temporaryWorkingDirectory.Exists)
            {
                temporaryWorkingDirectory.Delete(true);
            }
        }
    }

    [Test]
    // Directory.SetCurrentDirectory affects the entire test process, so this test cannot run in parallel.
    [NonParallelizable]
    public void AddDirectoryPatternResolvesPatternRelativeToAppBaseDirectory()
    {
        string originalWorkingDirectory = Directory.GetCurrentDirectory();
        string contentDirectoryName = $"content-{Guid.NewGuid():N}";
        string contentDirectoryPath = Path.Combine(AppContext.BaseDirectory, contentDirectoryName);
        DirectoryInfo temporaryWorkingDirectory = Directory.CreateTempSubdirectory("Pixely.Content.Tests-");

        try
        {
            Directory.CreateDirectory(contentDirectoryPath);
            File.WriteAllText(Path.Combine(contentDirectoryPath, "marker.txt"), "from app directory");
            Directory.SetCurrentDirectory(temporaryWorkingDirectory.FullName);

            using ContentSource contentSource = new ContentSourceBuilder()
                .AddDirectoryPattern(contentDirectoryName)
                .Create();
            using StreamReader reader = new(contentSource.OpenStream("marker.txt"));

            Assert.That(reader.ReadToEnd(), Is.EqualTo("from app directory"));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            if (Directory.Exists(contentDirectoryPath))
            {
                Directory.Delete(contentDirectoryPath, true);
            }
            if (temporaryWorkingDirectory.Exists)
            {
                temporaryWorkingDirectory.Delete(true);
            }
        }
    }

    [Test]
    public void AddDirectoryPatternAcceptsZeroMatches()
    {
        string pattern = $"missing-{Guid.NewGuid():N}-*";

        ContentSource contentSource = new ContentSourceBuilder()
            .AddDirectoryPattern(pattern)
            .Create();

        Assert.That(contentSource, Is.SameAs(DictionaryContentSource.Empty));
    }

    [Test]
    public void AddDirectoryPatternAddsMatchesInOrdinalOrder()
    {
        string prefix = $"content-{Guid.NewGuid():N}";
        string firstDirectoryPath = Path.Combine(AppContext.BaseDirectory, $"{prefix}-a");
        string lastDirectoryPath = Path.Combine(AppContext.BaseDirectory, $"{prefix}-z");

        try
        {
            Directory.CreateDirectory(lastDirectoryPath);
            File.WriteAllText(Path.Combine(lastDirectoryPath, "marker.txt"), "last");
            Directory.CreateDirectory(firstDirectoryPath);
            File.WriteAllText(Path.Combine(firstDirectoryPath, "marker.txt"), "first");

            using ContentSource contentSource = new ContentSourceBuilder()
                .AddDirectoryPattern($"{prefix}-*")
                .Create();
            using StreamReader reader = new(contentSource.OpenStream("marker.txt"));

            Assert.That(reader.ReadToEnd(), Is.EqualTo("last"));
        }
        finally
        {
            if (Directory.Exists(firstDirectoryPath))
            {
                Directory.Delete(firstDirectoryPath, true);
            }
            if (Directory.Exists(lastDirectoryPath))
            {
                Directory.Delete(lastDirectoryPath, true);
            }
        }
    }

    [Test]
    public void AddDefaultContent_WithArchiveAndDirectory_PreservesDirectoryPrecedence()
    {
        string contentName = $"content-{Guid.NewGuid():N}";
        string archivePath = Path.Combine(AppContext.BaseDirectory, $"{contentName}.pk3");
        string directoryPath = Path.Combine(AppContext.BaseDirectory, contentName);

        try
        {
            using (ZipArchive archive = System.IO.Compression.ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry("marker.txt");
                using StreamWriter writer = new(entry.Open());
                writer.Write("archive");
            }
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(Path.Combine(directoryPath, "marker.txt"), "directory");

            using ContentSource source = new ContentSourceBuilder().AddDefaultContent(contentName).Create();
            using StreamReader reader = new(source.OpenStream("marker.txt"));

            Assert.That(reader.ReadToEnd(), Is.EqualTo("directory"));
        }
        finally
        {
            File.Delete(archivePath);
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }
}
