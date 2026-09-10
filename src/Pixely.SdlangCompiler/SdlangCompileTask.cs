using Pixely.ShaderCommon;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Text.Json;

namespace Pixely.SdlangCompiler;

/// <summary>
/// MSBuild task for compiling Slang shaders for SDL3.
/// </summary>
public class SdlangCompileTask : Microsoft.Build.Utilities.Task
{
    private static readonly string[] GeneratedShaderExtensions = ["spv", "dxil", "metal", "wgsl"];

    /// <summary>
    /// The input shader file to compile. If empty or null, the task succeeds without compiling.
    /// </summary>
    public string? InputFile { get; set; }

    /// <summary>
    /// Path to the slangc executable. Slang is build-time tooling, so it is located through this
    /// parameter instead of being copied into the output of the project being built.
    /// Deliberately not [Required], so that a project still calling the task the old way gets the
    /// migration hint below instead of MSBuild's generic missing-parameter error.
    /// </summary>
    public string? SlangCompilerPath { get; set; }

    /// <summary>
    /// Files generated for the input shader.
    /// </summary>
    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = [];

    public override bool Execute()
    {
        GeneratedFiles = [];

        if (string.IsNullOrEmpty(InputFile))
        {
            return true;
        }

        if (string.IsNullOrEmpty(SlangCompilerPath))
        {
            Log.LogError(
                "SdlangCompileTask requires SlangCompilerPath. Projects normally no longer invoke this task " +
                "directly: delete your CompileSdlangShaders target and declare the shaders instead, for example " +
                "<ItemGroup><SdlangShader Include=\"Content\\shaders\\*.slang\" /></ItemGroup>. " +
                "If you need a custom target, pass SlangCompilerPath=\"$(SlangCompilerPath)\". See docs/shaders.md.");
            return false;
        }

        try
        {
            SdlangCompiler sdlangCompiler = new(SlangCompilerPath);
            sdlangCompiler.Compile([InputFile], false);
            GeneratedFiles = GetGeneratedFiles(InputFile);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError(ex.Message);
            return false;
        }
    }

    private static ITaskItem[] GetGeneratedFiles(string inputFile)
    {
        string inputPath = Path.GetFullPath(inputFile);
        string inputDirectory = Path.GetDirectoryName(inputPath)!;
        string generatedDirectory = Path.Combine(inputDirectory, ".generated");
        string generatedFilename = Path.GetFileNameWithoutExtension(inputPath);

        string metadataPath = Path.Combine(generatedDirectory, $"{generatedFilename}.metadata.json");
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException(
                $"Shader compilation did not produce the expected file {metadataPath}",
                metadataPath);
        }

        ShaderMetadataHeaderDto? metadata = JsonSerializer.Deserialize(
            File.ReadAllText(metadataPath),
            ShaderMetadataJsonContext.Default.ShaderMetadataHeaderDto);
        IEnumerable<string> generatedShaderFiles = metadata?.Kind == ShaderKindDto.Graphics
            ? new[] { "vertex", "fragment" }.SelectMany(stage =>
                GeneratedShaderExtensions.Select(extension =>
                    Path.Combine(generatedDirectory, $"{generatedFilename}.{stage}.{extension}")))
            : GeneratedShaderExtensions.Select(extension =>
                Path.Combine(generatedDirectory, $"{generatedFilename}.{extension}"));
        string[] generatedFiles = generatedShaderFiles.Append(metadataPath).ToArray();

        string? missingGeneratedFile = generatedFiles.FirstOrDefault(path => !File.Exists(path));
        if (missingGeneratedFile != null)
        {
            throw new FileNotFoundException(
                $"Shader compilation did not produce the expected file {missingGeneratedFile}",
                missingGeneratedFile);
        }

        return generatedFiles
            .Select(path => (ITaskItem)new TaskItem(path))
            .ToArray();
    }
}
