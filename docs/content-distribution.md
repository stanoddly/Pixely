# Content distribution

Pixely loads runtime content through a `VirtualFileSystem`. Content paths such as `shaders/terrain` do not identify a physical distribution format. A project can provide the same path from a directory, a ZIP archive, an assembly resource, or a composition of those sources.

## Runtime sources

The common content sources are `ServiceCollection` extensions, so they can be used by applications and packages through the same registration API. `PixelyAppBuilder` inherits `ServiceCollection`.

- `AddContentFromDirectory` adds one physical directory.
- `AddContentFromProjectDirectory` resolves a directory from the application output or project tree.
- `AddContentFromDirectoryPattern` adds matching directories beside the application.
- `AddContentFromZipPattern` adds matching ZIP files beside the application.
- `AddFileSystem` adds a `VirtualFileSystem`, including one created by `EmbeddedFileSystem.Create`.
- `AddFileSystemCache` caches the composed filesystem when the root provider is built.

Patterns are resolved relative to `AppContext.BaseDirectory`. Matching directory names are sorted ordinally. When multiple sources contain the same virtual path, the source registered last wins. Content configuration is root-only because the composed `VirtualFileSystem` is owned by the root provider; adding sources or cache configuration to a child service collection throws `InvalidOperationException`.

## Build and publish policy

Content producers do not choose how a consumer distributes their outputs. In particular, `Pixely.SdlangCompiler.SdlangCompileTask` compiles `@(SdlangShader)` and exposes the physical generated files as `@(SdlangShaderOutput)`. The consuming project decides whether those files are copied, embedded, or packaged.

Files generated during a build do not exist when MSBuild evaluates the project. A content pipeline that must include generated files therefore enumerates its content tree inside an execution-time target, after the producer has run. An evaluation-time item glob is not sufficient for a clean build.

Use one of these policies:

- **Loose directory:** copy the content tree to `$(OutDir)` for direct loading and iteration.
- **Embedded resources:** add generated files to `@(EmbeddedResource)` before `AssignTargetPaths`; suitable for content owned by a library.
- **ZIP archive:** package the tracked build content and register the archive through `@(ResolvedFileToPublish)`; suitable for an application-owned content bundle.

The policies are independent of file type. Generated shaders motivate the execution-time integration, but the same content tree can contain textures, fonts, audio, and data files.

## Tutorials

- [Embed generated shaders in an assembly](../tutorials/Pixely.Tutorials.EmbeddedContent/README.md)
- [Publish content in a ZIP archive](../tutorials/Pixely.Tutorials.ZipContent/README.md)

The embedded tutorial follows the policy used by `Pixely.Pencuil`. The ZIP tutorial follows the policy used by Nerudova: normal builds use a loose `Content` directory, while published builds use `Content.pk3`.

## Publish without building

`dotnet publish --no-build` does not refresh generated content. Run a normal build first. An embedded-content target should also test `$(NoBuild)` so publishing an existing assembly does not invoke its content producers. A ZIP target packages the tracked content from the preceding build.
