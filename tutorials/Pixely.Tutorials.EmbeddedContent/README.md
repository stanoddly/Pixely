# Embedded content

This tutorial compiles a nested Slang shader, embeds every generated output in the executable assembly, and loads the SPIR-V resource through Pixely's content source.

Run it from the repository root:

```bash
dotnet run --project tutorials/Pixely.Tutorials.EmbeddedContent
```

The MSBuild target derives each resource name relative to `Content`, so the runtime path remains `shaders/nested/.generated/tutorial.vertex.spv`. `EmbeddedContentSource.Create` exposes the assembly resources through the same paths used by directory and ZIP content sources.

All `SdlangShader` inputs in this example are beneath `ContentSourceDirectory`. A project that embeds outputs from another root must define the intended logical-name mapping for that root.

The `$(NoBuild)` condition allows `dotnet publish --no-build` to reuse the assembly produced by an earlier build without invoking the shader compiler.
