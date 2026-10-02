using Pixely.Content;

// Loads content the way UseDefaultContent() does and reports what it found, so the bundle runs under node without SDL. In the browser the
// archive is a PixelyBrowserVfsFile; BROWSER_CONTENT_EXTRA lists further target paths, comma-separated, read directly from the file system.
try
{
    using ContentSource content = new ContentSourceBuilder().AddDefaultContent().Create();
    using (StreamReader reader = new(content.OpenStream("greeting.txt")))
    {
        Console.WriteLine($"RESULT greeting {reader.ReadToEnd().Trim()}");
    }
    Console.WriteLine($"RESULT shader {content.TryGetFile("shaders/.generated/package.vertex.wgsl", out ContentFile? _)}");
    foreach (string path in (Environment.GetEnvironmentVariable("BROWSER_CONTENT_EXTRA") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        Console.WriteLine($"RESULT extra {path} {File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path)).Trim()}");
    }
    return 0;
}
catch (Exception exception)
{
    Console.WriteLine($"RESULT error {exception.Message}");
    return 1;
}
