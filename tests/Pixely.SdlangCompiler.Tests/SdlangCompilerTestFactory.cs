using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Pixely.SdlangCompiler.Tests;

internal static class SdlangCompilerTestFactory
{
    public static SdlangCompiler Create()
    {
        AssemblyMetadataAttribute? compilerPathAttribute = typeof(SdlangCompilerTestFactory).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "SlangCompilerPath");
        string compilerPath = compilerPathAttribute?.Value
            ?? throw new InvalidOperationException("SlangCompilerPath not found in assembly metadata");

        return new SdlangCompiler(compilerPath, new TaskLoggingHelper(new NullBuildEngine(), nameof(SdlangCompileTask)));
    }
}
