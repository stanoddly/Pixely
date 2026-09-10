using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Pixely.SdlangBuildIntegration.Tests;

[Category("BuildIntegration")]
[Explicit("Runs nested dotnet build and publish processes and must execute in isolation.")]
[NonParallelizable]
public class SdlangBuildIntegrationTests
{
    [Test]
    [Platform("Win")]
    public async Task CleanDeletesBuildTaskAssembliesAfterReusableNodeBuild()
    {
        string repositoryDirectory = GetRepositoryDirectory();
        string projectDirectory = Path.Combine(
            repositoryDirectory,
            "tests",
            "Pixely.SdlangBuildIntegration.Tests",
            "BuildIntegration");
        string projectPath = Path.Combine(projectDirectory, "BuildIntegration.csproj");
        string taskOutputDirectory = Path.Combine(
            repositoryDirectory,
            "src",
            "Pixely.SdlangCompiler",
            "bin",
            "Debug",
            "net10.0");
        string[] taskAssemblyPaths =
        [
            Path.Combine(taskOutputDirectory, "Pixely.SdlangCompiler.dll"),
            Path.Combine(taskOutputDirectory, "Pixely.ShaderCommon.dll")
        ];

        await RunDotnetAsync(repositoryDirectory, "build-server", "shutdown");
        DeleteDirectory(taskOutputDirectory);

        try
        {
            await RunDotnetAsync(projectDirectory, false, "build", projectPath, "--nologo", "-nr:true");

            Assert.That(taskAssemblyPaths, Has.All.Matches<string>(File.Exists));

            string cleanOutput = await RunDotnetAsync(
                projectDirectory,
                false,
                "clean",
                projectPath,
                "--nologo",
                "-nr:true");

            Assert.Multiple(() =>
            {
                Assert.That(cleanOutput, Does.Not.Contain("MSB3061"));
                Assert.That(taskAssemblyPaths, Has.None.Matches<string>(File.Exists));
            });
        }
        finally
        {
            await RunDotnetAsync(repositoryDirectory, "build-server", "shutdown");
        }
    }

    [Test]
    public async Task CleanBuildAndPublishRouteGeneratedShaders()
    {
        string repositoryDirectory = GetRepositoryDirectory();
        string projectDirectory = Path.Combine(
            repositoryDirectory,
            "tests",
            "Pixely.SdlangBuildIntegration.Tests",
            "BuildIntegration");
        string projectPath = Path.Combine(projectDirectory, "BuildIntegration.csproj");
        string generatedDirectory = Path.Combine(projectDirectory, "Content", "shaders", ".generated");
        string nestedGeneratedDirectory = Path.Combine(projectDirectory, "Content", "shaders", "nested", ".generated");
        string externalGeneratedDirectory = Path.Combine(
            projectDirectory,
            "..",
            "ExternalShaders",
            ".generated");
        string outputDirectory = Path.Combine(projectDirectory, "bin");
        string intermediateDirectory = Path.Combine(projectDirectory, "obj");

        DeleteDirectory(generatedDirectory);
        DeleteDirectory(nestedGeneratedDirectory);
        DeleteDirectory(externalGeneratedDirectory);
        DeleteDirectory(outputDirectory);
        DeleteDirectory(intermediateDirectory);

        await RunDotnetAsync(projectDirectory, "build", projectPath, "--nologo");

        string buildGeneratedDirectory = Path.Combine(
            outputDirectory,
            "Debug",
            "net10.0",
            "Content",
            "shaders",
            ".generated");
        AssertGeneratedShadersExist(buildGeneratedDirectory);
        AssertExternalGeneratedShadersExist(externalGeneratedDirectory);
        AssertExternalGeneratedShadersAreNotCopied(Path.Combine(outputDirectory, "Debug", "net10.0"));
        AssertEmbeddedShadersAreNotCopied(Path.Combine(outputDirectory, "Debug", "net10.0"));
        AssertEmbeddedShadersExist(Path.Combine(outputDirectory, "Debug", "net10.0", "BuildIntegration.dll"));

        DeleteDirectory(generatedDirectory);
        DeleteDirectory(nestedGeneratedDirectory);
        DeleteDirectory(externalGeneratedDirectory);
        DeleteDirectory(outputDirectory);
        DeleteDirectory(intermediateDirectory);

        string publishDirectory = Path.Combine(outputDirectory, "publish");
        await RunDotnetAsync(
            projectDirectory,
            "publish",
            projectPath,
            "--nologo",
            "--output",
            publishDirectory);

        string publishGeneratedDirectory = Path.Combine(
            publishDirectory,
            "Content",
            "shaders",
            ".generated");
        AssertGeneratedShadersExist(publishGeneratedDirectory);
        AssertExternalGeneratedShadersExist(externalGeneratedDirectory);
        AssertExternalGeneratedShadersAreNotCopied(publishDirectory);
        AssertEmbeddedShadersAreNotCopied(publishDirectory);
        AssertEmbeddedShadersExist(Path.Combine(publishDirectory, "BuildIntegration.dll"));

        string noBuildPublishDirectory = Path.Combine(outputDirectory, "publish-no-build");
        await RunDotnetAsync(
            projectDirectory,
            "publish",
            projectPath,
            "--nologo",
            "--no-build",
            "--property:RejectUnexpectedShaderCompilation=true",
            "--output",
            noBuildPublishDirectory);

        string noBuildGeneratedDirectory = Path.Combine(
            noBuildPublishDirectory,
            "Content",
            "shaders",
            ".generated");
        AssertGeneratedShadersExist(noBuildGeneratedDirectory);
        AssertExternalGeneratedShadersExist(externalGeneratedDirectory);
        AssertExternalGeneratedShadersAreNotCopied(noBuildPublishDirectory);
        AssertEmbeddedShadersAreNotCopied(noBuildPublishDirectory);
        AssertEmbeddedShadersExist(Path.Combine(noBuildPublishDirectory, "BuildIntegration.dll"));
    }

    [Test]
    public async Task PublishPackagesGeneratedShadersInZip()
    {
        string repositoryDirectory = GetRepositoryDirectory();
        string projectDirectory = Path.Combine(
            repositoryDirectory,
            "tests",
            "Pixely.SdlangBuildIntegration.Tests",
            "ZipBuildIntegration");
        string projectPath = Path.Combine(projectDirectory, "ZipBuildIntegration.csproj");
        string generatedDirectory = Path.Combine(projectDirectory, "Content", "shaders", ".generated");
        string transientContentPath = Path.Combine(projectDirectory, "Content", "transient.txt");
        string outputDirectory = Path.Combine(projectDirectory, "bin");
        string intermediateDirectory = Path.Combine(projectDirectory, "obj");

        DeleteDirectory(generatedDirectory);
        DeleteDirectory(outputDirectory);
        DeleteDirectory(intermediateDirectory);

        try
        {
            Directory.CreateDirectory(generatedDirectory);
            File.WriteAllBytes(Path.Combine(generatedDirectory, "obsolete.spv"), [0]);
            File.WriteAllText(transientContentPath, "transient");

            string publishDirectory = Path.Combine(outputDirectory, "publish");
            await RunDotnetAsync(
                projectDirectory,
                "publish",
                projectPath,
                "--nologo",
                "--output",
                publishDirectory);

            string archivePath = Path.Combine(publishDirectory, "Content.pk3");
            AssertPackagedShadersExist(archivePath);
            AssertArchiveDoesNotContain(archivePath, "shaders/.generated/obsolete.spv");
            AssertArchiveContains(archivePath, "transient.txt");
            Assert.That(Directory.Exists(Path.Combine(publishDirectory, "Content")), Is.False);

            File.Delete(transientContentPath);

            string publishAfterDeleteDirectory = Path.Combine(outputDirectory, "publish-after-delete");
            await RunDotnetAsync(
                projectDirectory,
                "publish",
                projectPath,
                "--nologo",
                "--output",
                publishAfterDeleteDirectory);

            string archiveAfterDeletePath = Path.Combine(publishAfterDeleteDirectory, "Content.pk3");
            AssertPackagedShadersExist(archiveAfterDeletePath);
            AssertArchiveDoesNotContain(archiveAfterDeletePath, "transient.txt");
            Assert.That(Directory.Exists(Path.Combine(publishAfterDeleteDirectory, "Content")), Is.False);

            string noBuildPublishDirectory = Path.Combine(outputDirectory, "publish-no-build");
            await RunDotnetAsync(
                projectDirectory,
                "publish",
                projectPath,
                "--nologo",
                "--no-build",
                "--property:RejectUnexpectedShaderCompilation=true",
                "--output",
                noBuildPublishDirectory);

            string noBuildArchivePath = Path.Combine(noBuildPublishDirectory, "Content.pk3");
            AssertPackagedShadersExist(noBuildArchivePath);
            AssertArchiveDoesNotContain(noBuildArchivePath, "transient.txt");
            Assert.That(Directory.Exists(Path.Combine(noBuildPublishDirectory, "Content")), Is.False);
        }
        finally
        {
            File.Delete(transientContentPath);
        }
    }

    private static void AssertGeneratedShadersExist(string generatedDirectory)
    {
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.vertex.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.vertex.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.vertex.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.vertex.wgsl")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.fragment.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.fragment.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.fragment.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.fragment.wgsl")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "copy_output.metadata.json")), Is.True);
        });
    }

    private static void AssertEmbeddedShadersAreNotCopied(string outputDirectory)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFiles(outputDirectory, "embedded_output.vertex.*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(outputDirectory, "embedded_output.fragment.*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(outputDirectory, "embedded_output.metadata.json", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(outputDirectory, "embedded_nested.vertex.*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(outputDirectory, "embedded_nested.fragment.*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(outputDirectory, "embedded_nested.metadata.json", SearchOption.AllDirectories), Is.Empty);
        });
    }

    private static void AssertExternalGeneratedShadersExist(string generatedDirectory)
    {
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.vertex.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.vertex.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.vertex.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.vertex.wgsl")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.fragment.spv")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.fragment.dxil")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.fragment.metal")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.fragment.wgsl")), Is.True);
            Assert.That(File.Exists(Path.Combine(generatedDirectory, "external_output.metadata.json")), Is.True);
        });
    }

    private static void AssertExternalGeneratedShadersAreNotCopied(string outputDirectory)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFiles(outputDirectory, "external_output.vertex.*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(outputDirectory, "external_output.fragment.*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetFiles(outputDirectory, "external_output.metadata.json", SearchOption.AllDirectories), Is.Empty);
        });
    }

    private static void AssertEmbeddedShadersExist(string assemblyPath)
    {
        using FileStream assemblyStream = File.OpenRead(assemblyPath);
        using PEReader peReader = new PEReader(assemblyStream);
        MetadataReader metadataReader = peReader.GetMetadataReader();
        string[] resourceNames = metadataReader.ManifestResources
            .Select(handle => metadataReader.GetString(metadataReader.GetManifestResource(handle).Name))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.vertex.spv"));
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.vertex.dxil"));
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.vertex.metal"));
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.vertex.wgsl"));
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.fragment.spv"));
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.fragment.dxil"));
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.fragment.metal"));
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.fragment.wgsl"));
            Assert.That(resourceNames, Contains.Item("shaders/.generated/embedded_output.metadata.json"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.vertex.spv"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.vertex.dxil"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.vertex.metal"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.vertex.wgsl"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.fragment.spv"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.fragment.dxil"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.fragment.metal"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.fragment.wgsl"));
            Assert.That(resourceNames, Contains.Item("shaders/nested/.generated/embedded_nested.metadata.json"));
        });
    }

    private static void AssertPackagedShadersExist(string archivePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        string[] entryNames = archive.Entries
            .Select(entry => entry.FullName)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.vertex.spv"));
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.vertex.dxil"));
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.vertex.metal"));
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.vertex.wgsl"));
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.fragment.spv"));
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.fragment.dxil"));
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.fragment.metal"));
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.fragment.wgsl"));
            Assert.That(entryNames, Contains.Item("shaders/.generated/zip_output.metadata.json"));
        });
    }

    private static void AssertArchiveContains(string archivePath, string entryName)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        Assert.That(archive.Entries.Select(entry => entry.FullName), Contains.Item(entryName));
    }

    private static void AssertArchiveDoesNotContain(string archivePath, string entryName)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        Assert.That(archive.Entries.Select(entry => entry.FullName), Does.Not.Contain(entryName));
    }

    private static Task<string> RunDotnetAsync(string workingDirectory, params string[] arguments)
    {
        return RunDotnetAsync(workingDirectory, true, arguments);
    }

    private static async Task<string> RunDotnetAsync(
        string workingDirectory,
        bool disableNodeReuse,
        params string[] arguments)
    {
        ProcessStartInfo startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (disableNodeReuse)
        {
            startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        }
        else
        {
            startInfo.Environment.Remove("MSBUILDDISABLENODEREUSE");
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        string standardOutput = await standardOutputTask;
        string standardError = await standardErrorTask;

        Assert.That(
            process.ExitCode,
            Is.Zero,
            $"dotnet {string.Join(' ', arguments)} failed.{Environment.NewLine}" +
            standardOutput +
            standardError);

        return standardOutput + standardError;
    }

    private static string GetRepositoryDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pixely.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Pixely repository.");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
