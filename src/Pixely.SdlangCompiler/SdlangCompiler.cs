using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pixely.ShaderCommon;

namespace Pixely.SdlangCompiler;

internal enum ResourceType
{
    SampledTexture,
    StorageTexture,
    StorageBuffer,
    Sampler,
    UniformBuffer,
    ReadWriteStorageTexture,
    ReadWriteStorageBuffer
}

internal record struct ResourceBinding(string Name, ResourceType Type, int Space, int Index);

internal enum ShaderSourceKind
{
    Graphics,
    Compute
}

public class ShaderCompilationException(string message) : Exception(message);

public class ShaderBindingValidationException(string message) : Exception(message);

public class SdlangCompiler
{
    private const string GeneratedShaderDirectory = ".generated";
    private const int MaxReflectionTraversalDepth = 64;
    private static readonly string SlangVersion = GetSlangVersion();
    private static readonly ShaderFormatDto[] AdditionalTargetFormats =
        [ShaderFormatDto.Dxil, ShaderFormatDto.Msl];
    private static readonly ShaderFormatDto[] TargetFormats =
        [ShaderFormatDto.SpirV, .. AdditionalTargetFormats];

    private readonly string _slangCompilerPath;

    private static readonly Dictionary<ShaderFormatDto, string> TargetsWithExtensions = new()
    {
        { ShaderFormatDto.SpirV, "spv" },
        { ShaderFormatDto.Dxil, "dxil" },
        { ShaderFormatDto.Msl, "metal" }
    };

    /// <param name="slangCompilerPath">Path to the slangc executable to compile shaders with.</param>
    public SdlangCompiler(string slangCompilerPath)
    {
        if (!File.Exists(slangCompilerPath))
        {
            throw new FileNotFoundException($"slangc compiler not found at {slangCompilerPath}");
        }

        _slangCompilerPath = slangCompilerPath;
    }

    private static string GetSlangVersion()
    {
        AssemblyMetadataAttribute? attribute = typeof(SdlangCompiler).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SlangVersion");

        return attribute?.Value ?? throw new InvalidOperationException("SlangVersion not found in assembly metadata");
    }

    public void Compile(string[] filenames, bool force)
    {
        if (filenames.Length == 0)
        {
            throw new ShaderCompilationException("No filenames provided");
        }

        List<FileInfo> paths = filenames.Select(f => new FileInfo(f)).ToList();
        List<FileInfo> directories = paths.Where(p => Directory.Exists(p.FullName)).ToList();
        List<FileInfo> files = paths.Where(p => !Directory.Exists(p.FullName)).ToList();

        if (directories.Count > 0)
        {
            if (files.Count > 0)
            {
                Console.WriteLine("Warning: Ignoring files on command line because directories are present:");
                foreach (FileInfo file in files)
                {
                    Console.WriteLine($"  Ignored: {file.FullName}");
                }
            }

            foreach (FileInfo dir in directories)
            {
                FileInfo shaderFile = new FileInfo(Path.Combine(dir.FullName, "shader.slang"));
                if (!shaderFile.Exists)
                {
                    throw new ShaderCompilationException($"File {shaderFile.FullName} does not exist");
                }
                CompileShader(shaderFile, force);
            }
        }
        else
        {
            foreach (FileInfo file in files)
            {
                if (!file.Exists)
                {
                    throw new ShaderCompilationException($"File {file.FullName} does not exist");
                }
                CompileShader(file, force);
            }
        }
    }

    private static string CalculateSourceHash(FileInfo filePath, IEnumerable<string> sourceDependencies)
    {
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> pathLength = stackalloc byte[sizeof(int)];

        foreach (string sourceDependency in sourceDependencies)
        {
            byte[] dependencyPath = Encoding.UTF8.GetBytes(sourceDependency);
            BinaryPrimitives.WriteInt32LittleEndian(pathLength, dependencyPath.Length);
            sourceHash.AppendData(pathLength);
            sourceHash.AppendData(dependencyPath);

            FileInfo dependencyFile = ResolveSourceDependency(filePath, sourceDependency);
            using FileStream stream = dependencyFile.OpenRead();
            sourceHash.AppendData(SHA256.HashData(stream));
        }

        byte[] hash = sourceHash.GetHashAndReset();
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static FileInfo ResolveSourceDependency(FileInfo filePath, string sourceDependency)
    {
        string platformPath = sourceDependency.Replace('/', Path.DirectorySeparatorChar);
        string dependencyPath = Path.IsPathRooted(platformPath)
            ? platformPath
            : Path.Combine(filePath.Directory!.FullName, platformPath);
        return new FileInfo(Path.GetFullPath(dependencyPath));
    }

    private static string GetTargetString(ShaderFormatDto format) => format switch
    {
        ShaderFormatDto.SpirV => "spirv",
        ShaderFormatDto.Dxil => "dxil",
        ShaderFormatDto.Msl => "metal",
        _ => throw new ArgumentException($"Unsupported shader format: {format}")
    };

    private static readonly Dictionary<ShaderFormatDto, List<string>> CommandLineOptions = new()
    {
        { ShaderFormatDto.SpirV, [] },
        { ShaderFormatDto.Dxil, ["-profile", "sm_6_0"] },
        { ShaderFormatDto.Msl, [] }
    };

    private (FileInfo reflectionFile, FileInfo dependencyFile, List<ShaderInstanceDto> shaderInstances) CompileTargets(
        FileInfo filePath,
        DirectoryInfo tempDir,
        DirectoryInfo outputDir,
        string entryPoint,
        string outputName)
    {
        FileInfo reflectionFile = new FileInfo(Path.Combine(tempDir.FullName, $"{outputName}.reflection.json"));
        FileInfo dependencyFile = new FileInfo(Path.Combine(tempDir.FullName, $"{outputName}.dependencies.d"));

        List<ShaderInstanceDto> shaderInstances = new List<ShaderInstanceDto>();
        shaderInstances.Add(CompileTarget(
            filePath,
            outputDir,
            ShaderFormatDto.SpirV,
            entryPoint,
            outputName,
            reflectionFile,
            dependencyFile));

        foreach (ShaderFormatDto format in AdditionalTargetFormats)
        {
            shaderInstances.Add(CompileTarget(filePath, outputDir, format, entryPoint, outputName));
        }

        return (reflectionFile, dependencyFile, shaderInstances);
    }

    private ShaderInstanceDto CompileTarget(
        FileInfo filePath,
        DirectoryInfo outputDir,
        ShaderFormatDto format,
        string entryPoint,
        string outputName,
        FileInfo? reflectionFile = null,
        FileInfo? dependencyFile = null)
    {
        string target = GetTargetString(format);
        string extension = TargetsWithExtensions[format];
        FileInfo outputFile = new FileInfo(Path.Combine(outputDir.FullName, $"{outputName}.{extension}"));
        List<string> args =
        [
            filePath.FullName,
            "-warnings-disable", "39001,39013,39029",
            "-target", target
        ];
        args.AddRange(CommandLineOptions[format]);
        args.AddRange(["-entry", entryPoint]);
        args.AddRange(["-o", outputFile.FullName]);
        if (reflectionFile != null && dependencyFile != null)
        {
            args.AddRange(["-reflection-json", reflectionFile.FullName]);
            args.AddRange(["-depfile", dependencyFile.FullName]);
        }

        ExecuteSlang(args, $"{format} shader compilation");
        if (format == ShaderFormatDto.Msl)
        {
            NormalizeMetalBufferBindings(outputFile);
        }

        return new ShaderInstanceDto(format, outputFile.Name, entryPoint);
    }

    private ShaderSourceKind DiscoverShader(
        FileInfo filePath,
        DirectoryInfo tempDir)
    {
        FileInfo reflectionFile = new FileInfo(Path.Combine(tempDir.FullName, "discovery.reflection.json"));
        List<string> args =
        [
            filePath.FullName,
            "-warnings-disable", "39001,39013,39029",
            "-target", "spirv",
            "-no-codegen",
            "-reflection-json", reflectionFile.FullName
        ];

        ExecuteSlang(args, "Shader discovery");
        return ParseDiscoveryReflection(reflectionFile);
    }

    private void ExecuteSlang(List<string> args, string operation)
    {
        Console.WriteLine($"Executing {operation.ToLowerInvariant()}: {_slangCompilerPath} {string.Join(" ", args)}");

        Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _slangCompilerPath,
                Arguments = string.Join(" ", args.Select(arg => arg.Contains(' ') ? $"\"{arg}\"" : arg)),
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        process.StandardInput.Close();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string diagnostic = string.IsNullOrWhiteSpace(standardError)
                ? "Slang did not provide an error message."
                : standardError.Trim();
            throw new ShaderCompilationException(
                $"{operation} failed with exit code {process.ExitCode}:{Environment.NewLine}{diagnostic}");
        }

        if (!string.IsNullOrEmpty(standardError))
        {
            Console.Error.Write(standardError);
        }
    }

    private static ShaderSourceKind ParseDiscoveryReflection(FileInfo reflectionFile)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(reflectionFile.FullName));
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("entryPoints", out JsonElement entryPointsElement))
        {
            throw new ShaderCompilationException("Shader source does not declare an entry point.");
        }

        List<JsonElement> entryPoints = entryPointsElement.EnumerateArray().ToList();
        List<JsonElement> computeEntryPoints = entryPoints.Where(entry => GetEntryPointStage(entry) == ShaderStageDto.Compute).ToList();
        List<JsonElement> vertexEntryPoints = entryPoints.Where(entry => GetEntryPointStage(entry) == ShaderStageDto.Vertex).ToList();
        List<JsonElement> fragmentEntryPoints = entryPoints.Where(entry => GetEntryPointStage(entry) == ShaderStageDto.Fragment).ToList();

        if (computeEntryPoints.Count > 0)
        {
            if (entryPoints.Count != 1 || computeEntryPoints.Count != 1)
            {
                throw new ShaderCompilationException("A compute shader source must declare exactly one compute entry point.");
            }

            ValidateEntryPointName(computeEntryPoints[0], "main");
            return ShaderSourceKind.Compute;
        }

        if (vertexEntryPoints.Count != 1)
        {
            throw new ShaderCompilationException(
                $"Graphics shader program must declare exactly one vertex entry point; found {vertexEntryPoints.Count}.");
        }

        if (fragmentEntryPoints.Count != 1)
        {
            throw new ShaderCompilationException(
                $"Graphics shader program must declare exactly one fragment entry point; found {fragmentEntryPoints.Count}.");
        }

        if (entryPoints.Count != 2)
        {
            throw new ShaderCompilationException("Graphics shader program contains unsupported additional entry points.");
        }

        JsonElement vertexEntryPoint = vertexEntryPoints[0];
        JsonElement fragmentEntryPoint = fragmentEntryPoints[0];
        ValidateEntryPointName(vertexEntryPoint, "vertexMain");
        ValidateEntryPointName(fragmentEntryPoint, "fragmentMain");

        JsonElement vertexOutputType = GetVertexOutputType(vertexEntryPoint);
        ValidatePositionField(vertexOutputType);
        JsonElement fragmentInputType = GetFragmentInputType(fragmentEntryPoint);
        ValidateGraphicsInterface(vertexOutputType, fragmentInputType);
        return ShaderSourceKind.Graphics;
    }

    private static ShaderStageDto GetEntryPointStage(JsonElement entryPoint)
    {
        string? stage = entryPoint.TryGetProperty("stage", out JsonElement stageElement)
            ? stageElement.GetString()?.ToLowerInvariant()
            : null;
        return stage switch
        {
            "vertex" => ShaderStageDto.Vertex,
            "fragment" or "pixel" => ShaderStageDto.Fragment,
            "compute" => ShaderStageDto.Compute,
            _ => throw new ShaderCompilationException($"Unknown shader stage '{stage}'.")
        };
    }

    private static void ValidateEntryPointName(JsonElement entryPoint, string expectedName)
    {
        string? actualName = entryPoint.TryGetProperty("name", out JsonElement nameElement)
            ? nameElement.GetString()
            : null;
        if (actualName != expectedName)
        {
            throw new ShaderCompilationException(
                $"{GetEntryPointStage(entryPoint)} entry point must be named '{expectedName}', but found '{actualName}'.");
        }
    }

    private static JsonElement GetVertexOutputType(JsonElement vertexEntryPoint)
    {
        if (!vertexEntryPoint.TryGetProperty("result", out JsonElement result) ||
            !result.TryGetProperty("type", out JsonElement resultType) ||
            !IsStructureType(resultType))
        {
            throw new ShaderCompilationException("Vertex entry point 'vertexMain' must return a named structure.");
        }

        return resultType;
    }

    private static JsonElement GetFragmentInputType(JsonElement fragmentEntryPoint)
    {
        if (!fragmentEntryPoint.TryGetProperty("parameters", out JsonElement parameters))
        {
            throw new ShaderCompilationException(
                "Fragment entry point 'fragmentMain' must have exactly one varying-input parameter.");
        }

        if (parameters.GetArrayLength() != 1 ||
            !parameters[0].TryGetProperty("type", out JsonElement inputType) ||
            !IsStructureType(inputType))
        {
            throw new ShaderCompilationException(
                "Fragment entry point 'fragmentMain' must have exactly one varying-input structure parameter.");
        }

        return inputType;
    }

    private static bool IsStructureType(JsonElement type)
    {
        return type.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "struct" &&
            type.TryGetProperty("name", out JsonElement name) && !string.IsNullOrEmpty(name.GetString());
    }

    private static void ValidateGraphicsInterface(JsonElement vertexOutputType, JsonElement fragmentInputType)
    {
        if (AreInterfaceTypesEquivalent(vertexOutputType, fragmentInputType))
        {
            return;
        }

        throw new ShaderCompilationException(
            "Fragment entry point 'fragmentMain' must consume the complete structure returned by " +
            $"'vertexMain'.{Environment.NewLine}" +
            $"Expected:{Environment.NewLine}{DescribeInterface(vertexOutputType)}{Environment.NewLine}" +
            $"Actual:{Environment.NewLine}{DescribeInterface(fragmentInputType)}");
    }

    private static bool AreInterfaceTypesEquivalent(JsonElement expected, JsonElement actual)
    {
        string? expectedKind = expected.TryGetProperty("kind", out JsonElement expectedKindElement)
            ? expectedKindElement.GetString()
            : null;
        string? actualKind = actual.TryGetProperty("kind", out JsonElement actualKindElement)
            ? actualKindElement.GetString()
            : null;
        if (expectedKind != actualKind)
        {
            return false;
        }

        switch (expectedKind)
        {
            case "scalar":
                return GetOptionalString(expected, "scalarType") == GetOptionalString(actual, "scalarType");
            case "vector":
            case "array":
                return GetOptionalInt32(expected, "elementCount") == GetOptionalInt32(actual, "elementCount") &&
                    expected.TryGetProperty("elementType", out JsonElement expectedElementType) &&
                    actual.TryGetProperty("elementType", out JsonElement actualElementType) &&
                    AreInterfaceTypesEquivalent(expectedElementType, actualElementType);
            case "matrix":
                return GetOptionalInt32(expected, "rowCount") == GetOptionalInt32(actual, "rowCount") &&
                    GetOptionalInt32(expected, "columnCount") == GetOptionalInt32(actual, "columnCount") &&
                    expected.TryGetProperty("elementType", out JsonElement expectedMatrixElementType) &&
                    actual.TryGetProperty("elementType", out JsonElement actualMatrixElementType) &&
                    AreInterfaceTypesEquivalent(expectedMatrixElementType, actualMatrixElementType);
            case "struct":
                return AreInterfaceFieldsEquivalent(expected, actual);
            default:
                return false;
        }
    }

    private static bool AreInterfaceFieldsEquivalent(JsonElement expected, JsonElement actual)
    {
        if (!expected.TryGetProperty("fields", out JsonElement expectedFields) ||
            !actual.TryGetProperty("fields", out JsonElement actualFields) ||
            expectedFields.GetArrayLength() != actualFields.GetArrayLength())
        {
            return false;
        }

        for (int index = 0; index < expectedFields.GetArrayLength(); index++)
        {
            JsonElement expectedField = expectedFields[index];
            JsonElement actualField = actualFields[index];
            if (GetOptionalString(expectedField, "name") != GetOptionalString(actualField, "name") ||
                !string.Equals(
                    GetOptionalString(expectedField, "semanticName"),
                    GetOptionalString(actualField, "semanticName"),
                    StringComparison.OrdinalIgnoreCase) ||
                GetOptionalInt32(expectedField, "semanticIndex", 0) !=
                    GetOptionalInt32(actualField, "semanticIndex", 0) ||
                !expectedField.TryGetProperty("type", out JsonElement expectedFieldType) ||
                !actualField.TryGetProperty("type", out JsonElement actualFieldType) ||
                !AreInterfaceTypesEquivalent(expectedFieldType, actualFieldType))
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) ? property.GetString() : null;
    }

    private static int GetOptionalInt32(JsonElement element, string propertyName, int defaultValue = -1)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            ? property.GetInt32()
            : defaultValue;
    }

    private static string DescribeInterface(JsonElement type)
    {
        if (!type.TryGetProperty("fields", out JsonElement fields))
        {
            return "  <not a structure>";
        }

        return string.Join(
            Environment.NewLine,
            fields.EnumerateArray().Select(field =>
            {
                string fieldType = field.TryGetProperty("type", out JsonElement reflectedType)
                    ? DescribeInterfaceType(reflectedType)
                    : "unknown";
                string fieldName = GetOptionalString(field, "name") ?? "<unnamed>";
                string? semanticName = GetOptionalString(field, "semanticName");
                int semanticIndex = GetOptionalInt32(field, "semanticIndex", 0);
                string semantic = semanticName == null
                    ? string.Empty
                    : $" : {semanticName}{(semanticIndex == 0 ? string.Empty : semanticIndex)}";
                return $"  {fieldType} {fieldName}{semantic}";
            }));
    }

    private static string DescribeInterfaceType(JsonElement type)
    {
        string? kind = GetOptionalString(type, "kind");
        switch (kind)
        {
            case "scalar":
                return DescribeScalarType(GetOptionalString(type, "scalarType"));
            case "vector":
                return type.TryGetProperty("elementType", out JsonElement vectorElementType)
                    ? $"{DescribeInterfaceType(vectorElementType)}{GetOptionalInt32(type, "elementCount")}"
                    : "vector";
            case "matrix":
                return type.TryGetProperty("elementType", out JsonElement matrixElementType)
                    ? $"{DescribeInterfaceType(matrixElementType)}{GetOptionalInt32(type, "rowCount")}x" +
                        GetOptionalInt32(type, "columnCount")
                    : "matrix";
            case "array":
                return type.TryGetProperty("elementType", out JsonElement arrayElementType)
                    ? $"{DescribeInterfaceType(arrayElementType)}[{GetOptionalInt32(type, "elementCount")}]"
                    : "array";
            case "struct":
                return GetOptionalString(type, "name") ?? "struct";
            default:
                return kind ?? "unknown";
        }
    }

    private static string DescribeScalarType(string? scalarType)
    {
        return scalarType switch
        {
            "float16" => "half",
            "float32" => "float",
            "float64" => "double",
            "int32" => "int",
            "uint32" => "uint",
            "bool" => "bool",
            _ => scalarType ?? "unknown"
        };
    }

    private static void ValidatePositionField(JsonElement vertexOutputType)
    {
        if (!vertexOutputType.TryGetProperty("fields", out JsonElement fields))
        {
            throw new ShaderCompilationException(
                "Vertex output structure must contain 'float4 Position : SV_Position' as its first field.");
        }

        List<JsonElement> positionFields = fields.EnumerateArray()
            .Where(field =>
                field.TryGetProperty("semanticName", out JsonElement semanticName) &&
                string.Equals(semanticName.GetString(), "SV_POSITION", StringComparison.OrdinalIgnoreCase))
            .ToList();
        bool positionIsFirst = fields.GetArrayLength() > 0 &&
            fields[0].TryGetProperty("semanticName", out JsonElement firstSemanticName) &&
            string.Equals(firstSemanticName.GetString(), "SV_POSITION", StringComparison.OrdinalIgnoreCase);
        bool hasValidPosition = positionFields.Count == 1 &&
            positionFields[0].TryGetProperty("type", out JsonElement positionType) &&
            IsFloat4(positionType) &&
            (!positionFields[0].TryGetProperty("semanticIndex", out JsonElement semanticIndex) ||
                semanticIndex.GetInt32() == 0);
        if (!positionIsFirst || !hasValidPosition)
        {
            throw new ShaderCompilationException(
                "Vertex output structure must contain 'float4 Position : SV_Position' exactly once and as its first field.");
        }
    }

    private static bool IsFloat4(JsonElement type)
    {
        return type.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "vector" &&
            type.TryGetProperty("elementCount", out JsonElement elementCount) && elementCount.GetInt32() == 4 &&
            type.TryGetProperty("elementType", out JsonElement elementType) &&
            elementType.TryGetProperty("kind", out JsonElement elementKind) && elementKind.GetString() == "scalar" &&
            elementType.TryGetProperty("scalarType", out JsonElement scalarType) && scalarType.GetString() == "float32";
    }

    private static List<string> ReadSourceDependencies(FileInfo filePath, FileInfo dependencyFile)
    {
        HashSet<string> sourceDependencies = new HashSet<string>(StringComparer.Ordinal);
        string sourceDirectoryPath = ResolvePhysicalPath(filePath.Directory!);

        foreach (string statement in File.ReadLines(dependencyFile.FullName))
        {
            int separatorIndex = FindDependencySeparator(statement);
            if (separatorIndex < 0)
            {
                throw new ShaderCompilationException($"Invalid dependency statement: {statement}");
            }

            foreach (string dependency in ParseDependencyPaths(statement.AsSpan(separatorIndex + 1)))
            {
                string absolutePath = ResolvePhysicalPath(new FileInfo(Path.GetFullPath(dependency)));
                string relativePath = Path.GetRelativePath(sourceDirectoryPath, absolutePath);
                string normalizedPath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
                sourceDependencies.Add(normalizedPath);
            }
        }

        string normalizedInputPath = Path.GetRelativePath(sourceDirectoryPath, ResolvePhysicalPath(filePath))
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!sourceDependencies.Contains(normalizedInputPath))
        {
            throw new ShaderCompilationException(
                $"Dependency output for {filePath.FullName} does not contain the input shader");
        }

        return sourceDependencies.Order(StringComparer.Ordinal).ToList();
    }

    private static string ResolvePhysicalPath(FileSystemInfo fileSystemInfo)
    {
        DirectoryInfo? parent = fileSystemInfo switch
        {
            FileInfo file => file.Directory,
            DirectoryInfo directory => directory.Parent,
            _ => throw new ArgumentException($"Unsupported file system entry: {fileSystemInfo.GetType().FullName}", nameof(fileSystemInfo))
        };
        if (parent is null)
        {
            return fileSystemInfo.FullName;
        }

        FileSystemInfo? target = fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true);
        return target is not null ? ResolvePhysicalPath(target) : Path.Combine(ResolvePhysicalPath(parent), fileSystemInfo.Name);
    }

    private static int FindDependencySeparator(string statement)
    {
        bool escaped = false;
        for (int index = 0; index < statement.Length; index++)
        {
            char character = statement[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == ':')
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string> ParseDependencyPaths(ReadOnlySpan<char> dependencyList)
    {
        List<string> dependencies = new List<string>();
        StringBuilder dependency = new StringBuilder();

        for (int index = 0; index < dependencyList.Length; index++)
        {
            char character = dependencyList[index];
            if (character == '\\')
            {
                if (++index >= dependencyList.Length)
                {
                    throw new ShaderCompilationException("Dependency path ends with an escape character");
                }

                dependency.Append(dependencyList[index]);
                continue;
            }

            if (character == '$' && index + 1 < dependencyList.Length && dependencyList[index + 1] == '$')
            {
                dependency.Append('$');
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (dependency.Length > 0)
                {
                    dependencies.Add(dependency.ToString());
                    dependency.Clear();
                }
                continue;
            }

            dependency.Append(character);
        }

        if (dependency.Length > 0)
        {
            dependencies.Add(dependency.ToString());
        }

        return dependencies;
    }

    private static void NormalizeMetalBufferBindings(FileInfo outputFile)
    {
        string metal = File.ReadAllText(outputFile.FullName);
        const string bufferAnnotationPattern = @"(?<decl>[^,\n()]+?\s+\w+\s*)\[\[buffer\((?<index>\d+)\)\]\]";

        List<Match> matches = Regex.Matches(metal, bufferAnnotationPattern, RegexOptions.CultureInvariant)
            .Cast<Match>()
            .ToList();

        Dictionary<int, int> replacementsByMatchIndex = new();
        List<(int MatchIndex, int OriginalIndex)> uniformBuffers = [];
        List<(int MatchIndex, int OriginalIndex)> storageBuffers = [];

        foreach ((Match match, int matchIndex) in matches.Select((m, i) => (m, i)))
        {
            string declaration = match.Groups["decl"].Value;
            int originalIndex = int.Parse(match.Groups["index"].Value);

            if (IsMetalUniformBufferDeclaration(declaration))
            {
                uniformBuffers.Add((matchIndex, originalIndex));
            }
            else if (IsMetalStorageBufferDeclaration(declaration))
            {
                storageBuffers.Add((matchIndex, originalIndex));
            }
        }

        foreach ((int matchIndex, int originalIndex) in uniformBuffers.OrderBy(b => b.OriginalIndex))
        {
            replacementsByMatchIndex[matchIndex] = originalIndex;
        }

        int uniformBufferCount = uniformBuffers.Count == 0 ? 0 : uniformBuffers.Max(b => b.OriginalIndex) + 1;
        foreach ((int matchIndex, int originalIndex) in storageBuffers.OrderBy(b => b.OriginalIndex))
        {
            replacementsByMatchIndex[matchIndex] = uniformBufferCount + originalIndex;
        }

        if (replacementsByMatchIndex.Count == 0)
        {
            return;
        }

        int currentMatchIndex = 0;
        string normalized = Regex.Replace(
            metal,
            bufferAnnotationPattern,
            match =>
            {
                int replacementIndex = replacementsByMatchIndex.TryGetValue(currentMatchIndex, out int replacement)
                    ? replacement
                    : int.Parse(match.Groups["index"].Value);
                currentMatchIndex++;
                return $"{match.Groups["decl"].Value}[[buffer({replacementIndex})]]";
            },
            RegexOptions.CultureInvariant);

        File.WriteAllText(outputFile.FullName, normalized);
    }

    private static bool IsMetalUniformBufferDeclaration(string declaration)
    {
        return declaration.Contains(" constant*", StringComparison.Ordinal)
            || declaration.Contains(" constant *", StringComparison.Ordinal);
    }

    private static bool IsMetalStorageBufferDeclaration(string declaration)
    {
        return declaration.Contains(" device*", StringComparison.Ordinal)
            || declaration.Contains(" device *", StringComparison.Ordinal);
    }

    private static void ValidateBindings(ShaderStageDto stage, List<ResourceBinding> bindings)
    {
        // Determine expected spaces based on shader stage.
        // SDL GPU Vulkan backend descriptor set layout:
        // - Vertex: readonly resources in space 0, uniforms in space 1
        // - Fragment: readonly resources in space 2, uniforms in space 3
        // - Compute: readonly resources in space 0, readwrite resources in space 1, uniforms in space 2
        int expectedReadOnlyResourceSpace;
        int expectedReadWriteResourceSpace;
        int expectedUniformSpace;
        string stageName;

        switch (stage)
        {
            case ShaderStageDto.Vertex:
                expectedReadOnlyResourceSpace = 0;
                expectedReadWriteResourceSpace = -1;
                expectedUniformSpace = 1;
                stageName = "vertex";
                break;
            case ShaderStageDto.Fragment:
                expectedReadOnlyResourceSpace = 2;
                expectedReadWriteResourceSpace = -1;
                expectedUniformSpace = 3;
                stageName = "fragment";
                break;
            case ShaderStageDto.Compute:
                expectedReadOnlyResourceSpace = 0;
                expectedReadWriteResourceSpace = 1;
                expectedUniformSpace = 2;
                stageName = "compute";
                break;
            default:
                throw new InvalidOperationException($"Unknown shader stage: {stage}");
        }

        foreach (ResourceBinding binding in bindings)
        {
            int expectedSpace;
            if (binding.Type == ResourceType.UniformBuffer)
            {
                expectedSpace = expectedUniformSpace;
            }
            else if (binding.Type == ResourceType.ReadWriteStorageTexture || binding.Type == ResourceType.ReadWriteStorageBuffer)
            {
                expectedSpace = expectedReadWriteResourceSpace;
            }
            else
            {
                expectedSpace = expectedReadOnlyResourceSpace;
            }

            if (binding.Space != expectedSpace)
            {
                throw new ShaderBindingValidationException(
                    $"Parameter '{binding.Name}' in {stageName} shader uses space {binding.Space}, " +
                    $"but SDL GPU requires space {expectedSpace} for {GetResourceTypeName(binding.Type)}");
            }
        }

        // Validate index ordering within the resource space
        // Read-only resources: sampled textures, then storage textures, then storage buffers
        // Read-write resources (compute only): separate index space — readwrite storage textures, then readwrite storage buffers
        List<ResourceBinding> readOnlyResourceBindings = bindings
            .Where(b => b.Type != ResourceType.UniformBuffer && b.Type != ResourceType.Sampler
                && b.Type != ResourceType.ReadWriteStorageTexture && b.Type != ResourceType.ReadWriteStorageBuffer)
            .OrderBy(b => b.Index)
            .ToList();

        List<ResourceBinding> sampledTextures = readOnlyResourceBindings.Where(b => b.Type == ResourceType.SampledTexture).ToList();
        List<ResourceBinding> storageTextures = readOnlyResourceBindings.Where(b => b.Type == ResourceType.StorageTexture).ToList();
        List<ResourceBinding> storageBuffers = readOnlyResourceBindings.Where(b => b.Type == ResourceType.StorageBuffer).ToList();

        int expectedIndex = 0;

        // Validate sampled textures come first and are contiguous starting at 0
        foreach (ResourceBinding tex in sampledTextures.OrderBy(t => t.Index))
        {
            if (tex.Index != expectedIndex)
            {
                throw new ShaderBindingValidationException(
                    $"Sampled texture '{tex.Name}' has index {tex.Index}, but expected {expectedIndex}. " +
                    $"SDL GPU requires sampled textures at indices 0..N-1");
            }
            expectedIndex++;
        }

        // Validate storage textures come next
        foreach (ResourceBinding tex in storageTextures.OrderBy(t => t.Index))
        {
            if (tex.Index != expectedIndex)
            {
                throw new ShaderBindingValidationException(
                    $"Storage texture '{tex.Name}' has index {tex.Index}, but expected {expectedIndex}. " +
                    $"SDL GPU requires storage textures immediately after sampled textures");
            }
            expectedIndex++;
        }

        // Validate storage buffers come after storage textures
        foreach (ResourceBinding buf in storageBuffers.OrderBy(b => b.Index))
        {
            if (buf.Index != expectedIndex)
            {
                throw new ShaderBindingValidationException(
                    $"Storage buffer '{buf.Name}' has index {buf.Index}, but expected {expectedIndex}. " +
                    $"SDL GPU requires storage buffers immediately after storage textures");
            }
            expectedIndex++;
        }

        // Read-write resources use a separate index space starting from 0
        List<ResourceBinding> readWriteStorageTextures = bindings.Where(b => b.Type == ResourceType.ReadWriteStorageTexture).ToList();
        List<ResourceBinding> readWriteStorageBuffers = bindings.Where(b => b.Type == ResourceType.ReadWriteStorageBuffer).ToList();

        int rwExpectedIndex = 0;

        // Validate read-write storage textures start at index 0 and are contiguous
        foreach (ResourceBinding tex in readWriteStorageTextures.OrderBy(t => t.Index))
        {
            if (tex.Index != rwExpectedIndex)
            {
                throw new ShaderBindingValidationException(
                    $"Read-write storage texture '{tex.Name}' has index {tex.Index}, but expected {rwExpectedIndex}.");
            }
            rwExpectedIndex++;
        }

        // Validate read-write storage buffers come after read-write storage textures
        foreach (ResourceBinding buf in readWriteStorageBuffers.OrderBy(b => b.Index))
        {
            if (buf.Index != rwExpectedIndex)
            {
                throw new ShaderBindingValidationException(
                    $"Read-write storage buffer '{buf.Name}' has index {buf.Index}, but expected {rwExpectedIndex}.");
            }
            rwExpectedIndex++;
        }

        ValidateSamplerTexturePairings(stageName, bindings);
    }

    private static void ValidateSamplerTexturePairings(string stageName, List<ResourceBinding> bindings)
    {
        List<ResourceBinding> sampledTextures = bindings.Where(b => b.Type == ResourceType.SampledTexture).ToList();
        List<ResourceBinding> samplers = bindings.Where(b => b.Type == ResourceType.Sampler).ToList();

        foreach (ResourceBinding sampledTexture in sampledTextures)
        {
            bool hasMatchingSampler = samplers.Any(s => s.Space == sampledTexture.Space && s.Index == sampledTexture.Index);
            if (!hasMatchingSampler)
            {
                throw new ShaderBindingValidationException(
                    $"Sampled texture '{sampledTexture.Name}' in {stageName} shader uses index {sampledTexture.Index} in space {sampledTexture.Space}, " +
                    "but SDL GPU requires a sampler at the same index and space");
            }
        }

        foreach (ResourceBinding sampler in samplers)
        {
            bool hasMatchingTexture = sampledTextures.Any(t => t.Space == sampler.Space && t.Index == sampler.Index);
            if (!hasMatchingTexture)
            {
                throw new ShaderBindingValidationException(
                    $"Sampler '{sampler.Name}' in {stageName} shader uses index {sampler.Index} in space {sampler.Space}, " +
                    "but SDL GPU requires a sampled texture at the same index and space");
            }
        }
    }

    private static string GetResourceTypeName(ResourceType type) => type switch
    {
        ResourceType.SampledTexture => "sampled textures",
        ResourceType.StorageTexture => "storage textures",
        ResourceType.StorageBuffer => "storage buffers",
        ResourceType.Sampler => "samplers",
        ResourceType.UniformBuffer => "uniform buffers",
        ResourceType.ReadWriteStorageTexture => "read-write storage textures",
        ResourceType.ReadWriteStorageBuffer => "read-write storage buffers",
        _ => type.ToString()
    };

    private static (string entryPoint, ShaderStageDto stage, ShaderBindingLayout resources, ShaderSystemValueInputs systemValueInputs, uint threadCountX, uint threadCountY, uint threadCountZ) ParseReflectionData(
        FileInfo reflectionFile,
        string expectedEntryPoint)
    {
        string json = File.ReadAllText(reflectionFile.FullName);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string entryPoint = "main";
        ShaderStageDto stage = ShaderStageDto.Vertex;
        uint threadCountX = 1;
        uint threadCountY = 1;
        uint threadCountZ = 1;

        JsonElement selectedEntryPoint = default;
        bool hasSelectedEntryPoint = false;
        if (root.TryGetProperty("entryPoints", out JsonElement entryPoints))
        {
            foreach (JsonElement candidate in entryPoints.EnumerateArray())
            {
                if (candidate.TryGetProperty("name", out JsonElement candidateName) &&
                    candidateName.GetString() == expectedEntryPoint)
                {
                    selectedEntryPoint = candidate;
                    hasSelectedEntryPoint = true;
                    break;
                }
            }
        }

        if (!hasSelectedEntryPoint)
        {
            throw new ShaderCompilationException(
                $"Reflection output does not contain entry point '{expectedEntryPoint}'.");
        }

        if (selectedEntryPoint.TryGetProperty("name", out JsonElement nameElement))
        {
            entryPoint = nameElement.GetString() ?? expectedEntryPoint;
        }

        if (selectedEntryPoint.TryGetProperty("stage", out JsonElement stageElement))
        {
            string? stageStr = stageElement.GetString()?.ToLower();
            stage = stageStr switch
            {
                "vertex" => ShaderStageDto.Vertex,
                "fragment" or "pixel" => ShaderStageDto.Fragment,
                "compute" => ShaderStageDto.Compute,
                _ => throw new InvalidOperationException($"Unknown shader stage '{stageStr}'")
            };
        }

        if (selectedEntryPoint.TryGetProperty("threadGroupSize", out JsonElement threadGroupSize))
        {
            JsonElement.ArrayEnumerator enumerator = threadGroupSize.EnumerateArray();
            if (enumerator.MoveNext())
            {
                threadCountX = enumerator.Current.GetUInt32();
            }
            if (enumerator.MoveNext())
            {
                threadCountY = enumerator.Current.GetUInt32();
            }
            if (enumerator.MoveNext())
            {
                threadCountZ = enumerator.Current.GetUInt32();
            }
        }

        ShaderSystemValueInputs systemValueInputs = AnalyzeSystemValueInputs(selectedEntryPoint);
        ShaderUniformSlotSizes shaderUniformSlots = new();

        byte samplers = 0;
        byte storageTextures = 0;
        byte storageBuffers = 0;
        byte readWriteStorageTextures = 0;
        byte readWriteStorageBuffers = 0;

        Dictionary<int, uint> storageBufferElementSizesBySlot = new();
        Dictionary<int, uint> readWriteStorageBufferElementSizesBySlot = new();

        List<ResourceBinding> resourceBindings = new();

        HashSet<string> usedParameterNames = GetUsedParameterNames(selectedEntryPoint);
        if (root.TryGetProperty("parameters", out JsonElement parameters))
        {
            foreach (JsonElement param in parameters.EnumerateArray())
            {
                string? reflectedParameterName = param.TryGetProperty("name", out JsonElement reflectedName)
                    ? reflectedName.GetString()
                    : null;
                if (reflectedParameterName == null || !usedParameterNames.Contains(reflectedParameterName))
                {
                    continue;
                }

                if (param.TryGetProperty("type", out JsonElement paramType) &&
                    paramType.TryGetProperty("kind", out JsonElement kindElement))
                {
                    string? kind = kindElement.GetString();
                    string paramName = param.TryGetProperty("name", out JsonElement nameEl)
                        ? nameEl.GetString() ?? "unknown"
                        : "unknown";
                    (int space, int index) = GetBindingInfo(param);

                    switch (kind)
                    {
                        case "samplerState":
                            samplers++;
                            resourceBindings.Add(new ResourceBinding(paramName, ResourceType.Sampler, space, index));
                            break;
                        case "resource":
                            if (paramType.TryGetProperty("baseShape", out JsonElement baseShapeElement))
                            {
                                string? baseShape = baseShapeElement.GetString();
                                bool isReadWrite = paramType.TryGetProperty("access", out JsonElement accessElement)
                                    && accessElement.GetString() == "readWrite";

                                if (baseShape == "structuredBuffer")
                                {
                                    uint elementSize = ComputeStructuredBufferElementSize(paramType);
                                    if (isReadWrite)
                                    {
                                        readWriteStorageBufferElementSizesBySlot[index] = elementSize;
                                        readWriteStorageBuffers++;
                                        resourceBindings.Add(new ResourceBinding(paramName, ResourceType.ReadWriteStorageBuffer, space, index));
                                    }
                                    else
                                    {
                                        storageBufferElementSizesBySlot[index] = elementSize;
                                        storageBuffers++;
                                        resourceBindings.Add(new ResourceBinding(paramName, ResourceType.StorageBuffer, space, index));
                                    }
                                }
                                else
                                {
                                    if (isReadWrite)
                                    {
                                        readWriteStorageTextures++;
                                        resourceBindings.Add(new ResourceBinding(paramName, ResourceType.ReadWriteStorageTexture, space, index));
                                    }
                                    else
                                    {
                                        // texture2D and other texture types are sampled textures
                                        resourceBindings.Add(new ResourceBinding(paramName, ResourceType.SampledTexture, space, index));
                                    }
                                }
                            }
                            break;
                        case "constantBuffer":
                            AdjustUniformBuffers(param, ref shaderUniformSlots);
                            resourceBindings.Add(new ResourceBinding(paramName, ResourceType.UniformBuffer, space, index));
                            break;
                    }
                }
            }
        }

        // Validate bindings conform to SDL GPU requirements
        ValidateBindings(stage, resourceBindings);

        ShaderBindingLayout shaderBindingLayout = new ShaderBindingLayout(
            new ShaderBindingCounts(samplers, storageTextures, storageBuffers, readWriteStorageTextures, readWriteStorageBuffers),
            shaderUniformSlots,
            BuildStorageBufferElementSizes(storageBufferElementSizesBySlot),
            BuildStorageBufferElementSizes(readWriteStorageBufferElementSizesBySlot));
        return (entryPoint, stage, shaderBindingLayout, systemValueInputs, threadCountX, threadCountY, threadCountZ);
    }

    internal static HashSet<string> GetUsedParameterNames(JsonElement entryPoint)
    {
        HashSet<string> usedParameterNames = new(StringComparer.Ordinal);
        string entryPointName = entryPoint.TryGetProperty("name", out JsonElement reflectedName) &&
            reflectedName.ValueKind == JsonValueKind.String
                ? reflectedName.GetString() ?? "unknown"
                : "unknown";
        if (!entryPoint.TryGetProperty("bindings", out JsonElement bindings) ||
            bindings.ValueKind != JsonValueKind.Array)
        {
            throw new ShaderCompilationException(
                $"Reflection entry point '{entryPointName}' does not contain a bindings array.");
        }

        foreach (JsonElement binding in bindings.EnumerateArray())
        {
            if (binding.ValueKind != JsonValueKind.Object ||
                !binding.TryGetProperty("name", out JsonElement name) ||
                name.ValueKind != JsonValueKind.String ||
                !binding.TryGetProperty("binding", out JsonElement bindingInfo) ||
                bindingInfo.ValueKind != JsonValueKind.Object ||
                !bindingInfo.TryGetProperty("used", out JsonElement used) ||
                used.ValueKind != JsonValueKind.Number ||
                !used.TryGetInt32(out int usedValue))
            {
                throw new ShaderCompilationException(
                    $"Reflection entry point '{entryPointName}' contains a malformed binding.");
            }

            if (usedValue == 0)
            {
                continue;
            }

            string? parameterName = name.GetString();
            if (string.IsNullOrEmpty(parameterName))
            {
                throw new ShaderCompilationException(
                    $"Reflection entry point '{entryPointName}' contains a binding without a name.");
            }

            usedParameterNames.Add(parameterName);
        }

        return usedParameterNames;
    }

    private static ShaderSystemValueInputs AnalyzeSystemValueInputs(JsonElement entryPoint)
    {
        bool usesVertexId = false;
        bool usesInstanceId = false;

        AnalyzeSystemValueInputs(entryPoint, ref usesVertexId, ref usesInstanceId, 0);

        return new ShaderSystemValueInputs(usesVertexId, usesInstanceId);
    }

    private static void AnalyzeSystemValueInputs(
        JsonElement element,
        ref bool usesVertexId,
        ref bool usesInstanceId,
        int depth)
    {
        if (depth > MaxReflectionTraversalDepth)
        {
            throw new ShaderCompilationException("Slang reflection JSON exceeds the maximum supported nesting depth.");
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("semanticName", out JsonElement semanticNameElement))
            {
                string? semanticName = semanticNameElement.GetString();
                if (string.Equals(semanticName, "SV_VERTEXID", StringComparison.OrdinalIgnoreCase))
                {
                    usesVertexId = true;
                }
                else if (string.Equals(semanticName, "SV_INSTANCEID", StringComparison.OrdinalIgnoreCase))
                {
                    usesInstanceId = true;
                }
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                AnalyzeSystemValueInputs(property.Value, ref usesVertexId, ref usesInstanceId, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                AnalyzeSystemValueInputs(item, ref usesVertexId, ref usesInstanceId, depth + 1);
            }
        }
    }

    private static (int space, int index) GetBindingInfo(JsonElement param)
    {
        int space = 0;
        int index = 0;

        if (param.TryGetProperty("binding", out JsonElement binding))
        {
            if (binding.TryGetProperty("space", out JsonElement spaceEl))
            {
                space = spaceEl.GetInt32();
            }
            if (binding.TryGetProperty("index", out JsonElement indexEl))
            {
                index = indexEl.GetInt32();
            }
        }

        return (space, index);
    }

    private static void AdjustUniformBuffers(JsonElement param, ref ShaderUniformSlotSizes shaderUniformSlots)
    {
        // For constant buffers, binding information is required
        if (!param.TryGetProperty("binding", out JsonElement binding))
        {
            throw new InvalidOperationException("constantBuffer parameter missing required 'binding' property");
        }

        if (!binding.TryGetProperty("index", out JsonElement indexElement))
        {
            throw new InvalidOperationException("constantBuffer binding missing required 'index' property");
        }

        if (!indexElement.TryGetInt32(out int slotIndex))
        {
            throw new InvalidOperationException("constantBuffer binding 'index' is not a valid integer");
        }

        // Extract size information from type.elementVarLayout.binding
        if (!param.TryGetProperty("type", out JsonElement typeElement))
        {
            throw new InvalidOperationException("constantBuffer parameter missing required 'type' property");
        }

        if (!typeElement.TryGetProperty("elementVarLayout", out JsonElement elementVarLayout))
        {
            throw new InvalidOperationException("constantBuffer type missing required 'elementVarLayout' property");
        }

        if (!elementVarLayout.TryGetProperty("binding", out JsonElement layoutBinding))
        {
            throw new InvalidOperationException("constantBuffer elementVarLayout missing required 'binding' property");
        }

        if (!layoutBinding.TryGetProperty("size", out JsonElement sizeElement))
        {
            throw new InvalidOperationException("constantBuffer layout binding missing required 'size' property");
        }

        if (!sizeElement.TryGetByte(out byte bufferSize))
        {
            throw new InvalidOperationException("constantBuffer layout binding 'size' is not a valid integer");
        }

        // Update the appropriate slot based on the index
        // Valid indices are 0-3 corresponding to Slot1-Slot4
        if (slotIndex < 0 || slotIndex > 3)
        {
            throw new InvalidOperationException($"constantBuffer slot index {slotIndex} is out of valid range [0-3]");
        }

        shaderUniformSlots = slotIndex switch
        {
            0 => shaderUniformSlots with { Slot0 = bufferSize },
            1 => shaderUniformSlots with { Slot1 = bufferSize },
            2 => shaderUniformSlots with { Slot2 = bufferSize },
            3 => shaderUniformSlots with { Slot3 = bufferSize },
            // TODO: error message
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static void WriteComputeMetadata(
        DirectoryInfo outputDir,
        string filenameWithoutExt,
        ShaderBindingLayout resources,
        List<ShaderInstanceDto> shaderInstances,
        string sourceHash,
        List<string> sourceDependencies,
        uint threadCountX,
        uint threadCountY,
        uint threadCountZ)
    {
        FileInfo metadataFile = new FileInfo(Path.Combine(outputDir.FullName, $"{filenameWithoutExt}.metadata.json"));
        ComputeShaderMetadataDto metadata = new ComputeShaderMetadataDto
        {
            BindingLayout = resources,
            Shaders = shaderInstances,
            SourceHash = sourceHash,
            SourceDependencies = sourceDependencies,
            SlangVersion = SlangVersion,
            ThreadCountX = threadCountX,
            ThreadCountY = threadCountY,
            ThreadCountZ = threadCountZ
        };
        using FileStream stream = metadataFile.Create();
        JsonSerializer.Serialize(stream, metadata, ShaderMetadataJsonContext.Default.ComputeShaderMetadataDto);
    }

    private static void WriteGraphicsMetadata(
        DirectoryInfo outputDir,
        string filenameWithoutExt,
        ShaderBindingLayout vertexBindingLayout,
        ShaderSystemValueInputs vertexSystemValueInputs,
        List<ShaderInstanceDto> vertexShaders,
        ShaderBindingLayout fragmentBindingLayout,
        List<ShaderInstanceDto> fragmentShaders,
        string sourceHash,
        List<string> sourceDependencies)
    {
        FileInfo metadataFile = new FileInfo(Path.Combine(outputDir.FullName, $"{filenameWithoutExt}.metadata.json"));
        GraphicsShaderProgramMetadataDto metadata = new GraphicsShaderProgramMetadataDto
        {
            Vertex = new GraphicsVertexShaderStageMetadataDto
            {
                BindingLayout = vertexBindingLayout,
                SystemValueInputs = vertexSystemValueInputs,
                Shaders = vertexShaders
            },
            Fragment = new GraphicsShaderStageMetadataDto
            {
                BindingLayout = fragmentBindingLayout,
                Shaders = fragmentShaders
            },
            SourceHash = sourceHash,
            SourceDependencies = sourceDependencies,
            SlangVersion = SlangVersion
        };

        using FileStream stream = metadataFile.Create();
        JsonSerializer.Serialize(stream, metadata, ShaderMetadataJsonContext.Default.GraphicsShaderProgramMetadataDto);
    }

    private static bool ShouldSkipCompilation(FileInfo filePath, DirectoryInfo outputDir, bool force)
    {
        if (force)
        {
            return false;
        }

        string filenameWithoutExt = Path.GetFileNameWithoutExtension(filePath.Name);
        FileInfo metadataFile = new FileInfo(Path.Combine(outputDir.FullName, $"{filenameWithoutExt}.metadata.json"));

        if (!metadataFile.Exists)
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(metadataFile.FullName);
            ShaderMetadataHeaderDto? metadata = JsonSerializer.Deserialize(json, ShaderMetadataJsonContext.Default.ShaderMetadataHeaderDto);

            if (metadata?.SourceHash == null || metadata.SourceDependencies is not { Count: > 0 })
            {
                return false;
            }

            // Force recompilation if slang version is missing or different
            if (metadata.SlangVersion == null || metadata.SlangVersion != SlangVersion)
            {
                return false;
            }

            if (metadata.Kind != ShaderKindDto.Graphics && metadata.Stage != ShaderStageDto.Compute)
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            List<(JsonElement Shaders, string SourceEntryPoint)> shaderCollections = new();
            if (metadata.Kind == ShaderKindDto.Graphics)
            {
                if (!root.TryGetProperty("vertex", out JsonElement vertex) ||
                    vertex.TryGetProperty("entryPoint", out JsonElement _) ||
                    !vertex.TryGetProperty("shaders", out JsonElement vertexShaders) ||
                    vertexShaders.ValueKind != JsonValueKind.Array ||
                    !root.TryGetProperty("fragment", out JsonElement fragment) ||
                    fragment.TryGetProperty("entryPoint", out JsonElement _) ||
                    !fragment.TryGetProperty("shaders", out JsonElement fragmentShaders) ||
                    fragmentShaders.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                shaderCollections.Add((vertexShaders, "vertexMain"));
                shaderCollections.Add((fragmentShaders, "fragmentMain"));
            }
            else
            {
                if (!root.TryGetProperty("shaders", out JsonElement shaders) || shaders.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                shaderCollections.Add((shaders, "main"));
            }

            foreach ((JsonElement shaders, string sourceEntryPoint) in shaderCollections)
            {
                HashSet<ShaderFormatDto> cachedFormats = [];
                foreach (JsonElement shader in shaders.EnumerateArray())
                {
                    if (!shader.TryGetProperty("format", out JsonElement formatElement) ||
                        formatElement.ValueKind != JsonValueKind.String ||
                        !Enum.TryParse(formatElement.GetString(), ignoreCase: true, out ShaderFormatDto format) ||
                        !cachedFormats.Add(format))
                    {
                        return false;
                    }

                    if (!shader.TryGetProperty("filename", out JsonElement filenameElement) ||
                        filenameElement.ValueKind != JsonValueKind.String ||
                        !shader.TryGetProperty("entryPoint", out JsonElement entryPointElement) ||
                        entryPointElement.ValueKind != JsonValueKind.String ||
                        entryPointElement.GetString() != GetGeneratedEntryPointName(format, sourceEntryPoint))
                    {
                        return false;
                    }

                    string? filename = filenameElement.GetString();
                    if (string.IsNullOrEmpty(filename) || !File.Exists(Path.Combine(outputDir.FullName, filename)))
                    {
                        return false;
                    }
                }

                if (!cachedFormats.SetEquals(TargetFormats))
                {
                    return false;
                }
            }

            string currentHash = CalculateSourceHash(filePath, metadata.SourceDependencies);
            return metadata.SourceHash == currentHash;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static StorageBufferElementSizes BuildStorageBufferElementSizes(Dictionary<int, uint> elementSizesBySlot)
    {
        elementSizesBySlot.TryGetValue(0, out uint slot0);
        elementSizesBySlot.TryGetValue(1, out uint slot1);
        elementSizesBySlot.TryGetValue(2, out uint slot2);
        elementSizesBySlot.TryGetValue(3, out uint slot3);
        return new StorageBufferElementSizes((ushort)slot0, (ushort)slot1, (ushort)slot2, (ushort)slot3);
    }

    private static uint ComputeStructuredBufferElementSize(JsonElement resourceType)
    {
        if (!resourceType.TryGetProperty("resultType", out JsonElement resultType))
        {
            return 0;
        }

        string? kind = resultType.TryGetProperty("kind", out JsonElement kindEl) ? kindEl.GetString() : null;

        if (kind == "struct")
        {
            if (!resultType.TryGetProperty("fields", out JsonElement fields))
            {
                return 0;
            }

            uint maxEnd = 0;
            foreach (JsonElement field in fields.EnumerateArray())
            {
                if (field.TryGetProperty("binding", out JsonElement binding))
                {
                    uint offset = binding.TryGetProperty("offset", out JsonElement offEl) ? offEl.GetUInt32() : 0;
                    uint size = binding.TryGetProperty("size", out JsonElement sizeEl) ? sizeEl.GetUInt32() : 0;
                    maxEnd = Math.Max(maxEnd, offset + size);
                }
            }
            return maxEnd;
        }

        if (kind == "vector")
        {
            uint elementCount = resultType.TryGetProperty("elementCount", out JsonElement countEl) ? countEl.GetUInt32() : 0;
            if (!resultType.TryGetProperty("elementType", out JsonElement elementType))
            {
                return 0;
            }
            return elementCount * GetScalarSize(elementType);
        }

        if (kind == "scalar")
        {
            return GetScalarSize(resultType);
        }

        return 0;
    }

    private static uint GetScalarSize(JsonElement typeElement)
    {
        if (!typeElement.TryGetProperty("scalarType", out JsonElement scalarTypeEl))
        {
            return 0;
        }

        // bool is 4 bytes in SPIR-V/DXIL storage buffers but 1 byte in MSL — size is backend-dependent,
        // so return 0 (skip validation) rather than hardcode a target-specific value.
        return scalarTypeEl.GetString() switch
        {
            "float32" or "int32" or "uint32" => 4,
            "float64" or "int64" or "uint64" => 8,
            "float16" or "int16" or "uint16" => 2,
            "int8" or "uint8" => 1,
            _ => 0
        };
    }

    private void CompileShader(FileInfo filePath, bool force = false)
    {
        DirectoryInfo parentDir = filePath.Directory!;
        DirectoryInfo outputDir = new DirectoryInfo(Path.Combine(parentDir.FullName, GeneratedShaderDirectory));

        if (ShouldSkipCompilation(filePath, outputDir, force))
        {
            CleanupGeneratedFiles(parentDir, outputDir);
            Console.WriteLine($"Skipping {filePath.FullName} (unchanged)");
            return;
        }

        Console.WriteLine($"Result directory: {outputDir.FullName}");

        string filenameWithoutExt = Path.GetFileNameWithoutExtension(filePath.Name);

        // Ensure output directory exists
        outputDir.Create();

        DirectoryInfo tempDir = Directory.CreateTempSubdirectory("ShaderPack_");
        try
        {
            Console.WriteLine($"Intermediate results written to: {tempDir.FullName}");

            ShaderSourceKind shaderSourceKind = DiscoverShader(filePath, tempDir);

            if (shaderSourceKind == ShaderSourceKind.Compute)
            {
                (FileInfo reflectionFile, FileInfo dependencyFile, List<ShaderInstanceDto> shaderInstances) = CompileTargets(
                    filePath,
                    tempDir,
                    outputDir,
                    "main",
                    filenameWithoutExt);
                List<string> sourceDependencies = ReadSourceDependencies(filePath, dependencyFile);
                string sourceHash = CalculateSourceHash(filePath, sourceDependencies);
                (string entryPoint, ShaderStageDto stage, ShaderBindingLayout bindingLayout, ShaderSystemValueInputs _, uint threadCountX, uint threadCountY, uint threadCountZ) =
                    ParseReflectionData(reflectionFile, "main");
                if (stage != ShaderStageDto.Compute)
                {
                    throw new ShaderCompilationException("Entry point 'main' is not a compute shader.");
                }

                shaderInstances = NormalizeEntryPointNames(shaderInstances, entryPoint);
                WriteComputeMetadata(
                    outputDir,
                    filenameWithoutExt,
                    bindingLayout,
                    shaderInstances,
                    sourceHash,
                    sourceDependencies,
                    threadCountX,
                    threadCountY,
                    threadCountZ);
                CleanupGeneratedFiles(parentDir, outputDir);
                return;
            }

            (FileInfo vertexReflectionFile, FileInfo graphicsDependencyFile, List<ShaderInstanceDto> vertexShaders) = CompileTargets(
                filePath,
                tempDir,
                outputDir,
                "vertexMain",
                $"{filenameWithoutExt}.vertex");
            List<string> graphicsSourceDependencies = ReadSourceDependencies(filePath, graphicsDependencyFile);
            string graphicsSourceHash = CalculateSourceHash(filePath, graphicsSourceDependencies);
            (string vertexEntryPoint, ShaderStageDto vertexStage, ShaderBindingLayout vertexBindingLayout, ShaderSystemValueInputs vertexSystemValueInputs, uint _, uint _, uint _) =
                ParseReflectionData(vertexReflectionFile, "vertexMain");
            if (vertexStage != ShaderStageDto.Vertex)
            {
                throw new ShaderCompilationException("Entry point 'vertexMain' is not a vertex shader.");
            }

            (FileInfo fragmentReflectionFile, FileInfo _, List<ShaderInstanceDto> fragmentShaders) = CompileTargets(
                filePath,
                tempDir,
                outputDir,
                "fragmentMain",
                $"{filenameWithoutExt}.fragment");
            (string fragmentEntryPoint, ShaderStageDto fragmentStage, ShaderBindingLayout fragmentBindingLayout, ShaderSystemValueInputs _, uint _, uint _, uint _) =
                ParseReflectionData(fragmentReflectionFile, "fragmentMain");
            if (fragmentStage != ShaderStageDto.Fragment)
            {
                throw new ShaderCompilationException("Entry point 'fragmentMain' is not a fragment shader.");
            }

            WriteGraphicsMetadata(
                outputDir,
                filenameWithoutExt,
                vertexBindingLayout,
                vertexSystemValueInputs,
                NormalizeEntryPointNames(vertexShaders, vertexEntryPoint),
                fragmentBindingLayout,
                NormalizeEntryPointNames(fragmentShaders, fragmentEntryPoint),
                graphicsSourceHash,
                graphicsSourceDependencies);
            CleanupGeneratedFiles(parentDir, outputDir);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    private static void CleanupGeneratedFiles(DirectoryInfo sourceDirectory, DirectoryInfo outputDirectory)
    {
        if (!outputDirectory.Exists)
        {
            return;
        }

        StringComparer filenameComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        HashSet<string> expectedFilenames = new HashSet<string>(filenameComparer);
        foreach (FileInfo metadataFile in outputDirectory.GetFiles("*.metadata.json"))
        {
            string shaderName = metadataFile.Name[..^".metadata.json".Length];
            if (!File.Exists(Path.Combine(sourceDirectory.FullName, $"{shaderName}.slang")))
            {
                continue;
            }

            ShaderMetadataHeaderDto? metadata;
            try
            {
                metadata = JsonSerializer.Deserialize(
                    File.ReadAllText(metadataFile.FullName),
                    ShaderMetadataJsonContext.Default.ShaderMetadataHeaderDto);
            }
            catch (JsonException)
            {
                continue;
            }

            IEnumerable<string>? outputNames = metadata?.Kind == ShaderKindDto.Graphics
                ? new[] { "vertex", "fragment" }.SelectMany(stage =>
                    TargetFormats.Select(format =>
                        $"{shaderName}.{stage}.{TargetsWithExtensions[format]}"))
                : metadata?.Stage == ShaderStageDto.Compute
                    ? TargetFormats.Select(format => $"{shaderName}.{TargetsWithExtensions[format]}")
                    : null;
            if (outputNames == null)
            {
                continue;
            }

            expectedFilenames.Add(metadataFile.Name);
            expectedFilenames.UnionWith(outputNames);
        }

        HashSet<string> generatedExtensions = new HashSet<string>(
            TargetsWithExtensions.Values.Select(extension => $".{extension}"),
            filenameComparer);
        foreach (FileInfo generatedFile in outputDirectory.GetFiles())
        {
            bool isGeneratedFile = generatedFile.Name.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase) ||
                generatedExtensions.Contains(generatedFile.Extension);
            if (isGeneratedFile && !expectedFilenames.Contains(generatedFile.Name))
            {
                Console.WriteLine($"Removing obsolete shader output: {generatedFile.FullName}");
                generatedFile.Delete();
            }
        }
    }

    private static List<ShaderInstanceDto> NormalizeEntryPointNames(
        IEnumerable<ShaderInstanceDto> shaderInstances,
        string entryPoint)
    {
        return shaderInstances.Select(instance =>
            new ShaderInstanceDto(
                instance.Format,
                instance.Filename,
                GetGeneratedEntryPointName(instance.Format, entryPoint))).ToList();
    }

    private static string GetGeneratedEntryPointName(ShaderFormatDto format, string sourceEntryPoint) => format switch
    {
        ShaderFormatDto.SpirV => "main",
        // Slang renames "main" to "main_0" in MSL output because "main" is reserved in C/C++.
        // Other source entry point names remain unchanged.
        ShaderFormatDto.Msl when sourceEntryPoint == "main" => "main_0",
        _ => sourceEntryPoint
    };

}
