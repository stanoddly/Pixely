# Shaders

Guide to writing and using shaders with Pixely. Shaders are written in Slang and compiled to SPIR-V, DXIL, MSL and WGSL at build time.

## File Structure

```
Content/shaders/
├── shader.slang          # Vertex and fragment entry points
└── .generated/           # Generated at build time
    ├── shader.vertex.spv
    ├── shader.vertex.dxil
    ├── shader.vertex.metal
    ├── shader.vertex.wgsl
    ├── shader.fragment.spv
    ├── shader.fragment.dxil
    ├── shader.fragment.metal
    ├── shader.fragment.wgsl
    └── shader.metadata.json
```

Shaders are automatically compiled during build. The build system generates SPIR-V binaries for Vulkan, DXIL binaries for Direct3D 12, MSL source for Metal, WGSL source for WebGPU in the browser, and metadata files in the `.generated/` directory.

## Build Integration

Reference the [Pixely SDK](sdk.md) and declare the shaders to compile:

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <Sdk Name="Pixely" Version="0.0.N" />

    <ItemGroup>
        <SdlangShader Include="Content\shaders\*.slang" />
    </ItemGroup>
</Project>
```

The SDK imports the shader build integration automatically. It compiles every `SdlangShader` item before `CoreCompile` and exposes the generated files as `@(SdlangShaderOutput)`. Generated files remain beside their shader sources; the compilation targets do not copy, package, or embed them. This lets each project own its complete content pipeline independently of shader compilation. The shader task and its dependencies are build tools and do not enter the application's references or output.

Generated shaders are runtime content. See [Content distribution](content-distribution.md) for the loose-directory, embedded-resource, and ZIP policies, with runnable tutorials for embedding generated shaders in an assembly and publishing content in a ZIP archive.

Pixely provides the shader compiler for Linux x64/ARM64, Windows x64, and macOS ARM64. Compilation uses the build host's compiler regardless of the application's target runtime. Custom compilation targets can use the compiler path exposed through `$(SlangCompilerPath)`.

### Custom compilation targets

The `SdlangShader` item plus the shared target covers the normal case. To compile from somewhere else, or at a different point in the build, invoke the task directly and pass the compiler path:

```xml
<Target Name="CompileGeneratedShaders" AfterTargets="CopyFilesToOutputDirectory" DependsOnTargets="ValidateSlangToolchain">
    <ItemGroup>
        <GeneratedShader Include="$(OutputPath)\Generated\*.slang" />
    </ItemGroup>
    <SdlangCompileTask InputFile="%(GeneratedShader.Identity)" SlangCompilerPath="$(SlangCompilerPath)" />
</Target>
```

Do not name a custom target `CompileSdlangShaders`; a target defined in the project overrides the imported one of the same name.

### Migrating existing projects

Earlier versions required each project to define its own target calling the task. Replace it with the `SdlangShader` item group shown above. Projects that still call the task without `SlangCompilerPath` fail the build with a message pointing here.

Remove `OutputItemType` and `OutputLogicalNamePrefix` metadata from `SdlangShader`. Projects that copy or package content should enumerate their content tree after compilation. Projects that embed generated shaders should consume `@(SdlangShaderOutput)` before `AssignTargetPaths`, as described in [Content distribution](content-distribution.md).

Only graphics-program and compute-shader sources belong in `SdlangShader`. Shared files consumed through `#include` or `import`, such as `common.slang`, remain excluded because they do not produce standalone runtime shaders.

Generated metadata records the normalized source dependencies and an aggregate source hash for each entry shader. Changing an included or imported source therefore recompiles every entry shader that consumes it. Slang's raw dependency file is a temporary compiler intermediate and is deleted after compilation.

## Basic Graphics Shader Program

```csharp
struct VertexInput
{
    float4 Position : TEXCOORD0;
};

struct VertexToFragment
{
    float4 Position : SV_Position;
};

[shader("vertex")]
VertexToFragment vertexMain(VertexInput input)
{
    VertexToFragment output;
    output.Position = input.Position;
    return output;
}

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    return float4(1.0, 0.0, 1.0, 1.0);  // Magenta
}
```

Every graphics-program source declares exactly one `vertexMain` and one `fragmentMain`. The vertex entry point returns a structure, and the fragment entry point receives the complete structure. `SV_Position` must be the first field. A fragment entry point cannot omit unused fields.

**Input semantics:** Use `TEXCOORD0`, `TEXCOORD1`, etc. for vertex attributes. These map to the vertex buffer configuration in `GraphicsPipelineBuilder`.

**Output semantics:** Always use `SV_Position` for the first vertex-output field. Use `SV_Target0`, `SV_Target1`, etc. for multiple render targets (MRT). Order matches the `AddColorTarget()` calls in pipeline builder.

## Constant Buffers (Uniforms)

Fragment shader with constant buffer:

```csharp
ConstantBuffer<float4> color : register(b0, space3);

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0
{
    return color;
}
```

**Register binding:** Use `register(b{slot}, space3)` where `{slot}` is 0-3. The slots a stage reads must start at 0 with no gaps. The compiler rejects a shader whose only constant buffer is `b1`, and it ignores constant buffers the stage never reads.

**Pushing data from C#:**

```csharp
// Before drawing, push uniform data
renderPass.PushFragmentUniformData(0, FColors.Magenta);  // Slot 0
```

**Available push methods:**
- `CommandBuffer.PushFragmentUniformData<T>(uint slot, T data)`
- `CommandBuffer.PushVertexUniformData<T>(uint slot, T data)`

**Slot limits:** 4 uniform slots per shader stage (0-3). Each slot can hold up to a certain size (check metadata).

## Storage Buffers and Resource Types

A storage buffer is a `StructuredBuffer<T>` or a `ByteAddressBuffer` on a `t` register. A sampled texture is `Texture2D` or another `Texture*` type on a `t` register, with its `SamplerState` on the `s` register of the same index. Compute shaders may also bind the read-write forms, `RWStructuredBuffer<T>`, `RWByteAddressBuffer` and `RWTexture2D<T>`, on `u` registers.

The reflection records the element size of a `StructuredBuffer<T>` and the runtime rejects a bound `GpuStorageBuffer<T>` whose element size differs. A `ByteAddressBuffer` has no element type, so its recorded size is 0 and the element-size check is skipped for its slot; any storage buffer may be bound to it.

SDL GPU has no binding slot for `Buffer<T>` and `RWBuffer<T>` (use a structured buffer) or for arrays of bindings such as `Texture2D textures[2]`, `SamplerState samplers[2]` and `ConstantBuffer<T> cbs[2]` (declare each element as its own parameter). The build fails with a `ShaderBindingValidationException` naming the parameter.

## Shader Stage Attribute

Always mark the two fixed entry points with the shader stage attribute:

```csharp
[shader("vertex")]
VertexToFragment vertexMain(VertexInput input);

[shader("fragment")]
float4 fragmentMain(VertexToFragment input) : SV_Target0;
```

## Loading Shaders

**Option 1: Direct path (most common)**

```csharp
GraphicsPipeline pipeline = graphicsPipelineBuilder
    .SetShaderProgram("shaders/shader")
    .Build();
```

The path is relative to the `Content/` directory and excludes the `.slang` extension. The loader reads the combined metadata and selects the first compiled format supported by the active GPU backend for each native stage.

**Option 2: Load once and reuse**

```csharp
GraphicsShaderProgram shaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/terrain");

GraphicsPipeline pipeline = graphicsPipelineBuilder
    .SetShaderProgram(shaderProgram)
    .Build();
```

Vertex and fragment stages cannot be loaded or composed independently.

### Depth-only programs

SDL3's GPU API currently requires a fragment stage for a depth-only pipeline, although its supported backends allow the stage to be omitted. This limitation is tracked by [SDL issue #12311](https://github.com/libsdl-org/SDL/issues/12311). Keep the workaround local to the graphics program by declaring a no-op fragment entry point with the same interface:

```csharp
[shader("fragment")]
void fragmentMain(VertexToFragment input)
{
}
```

See `Pixely.Tutorials.DepthOnly` for a complete example.

## GPU Backend Selection

Pixely lets SDL choose the GPU backend automatically by default. Register `PixelyConfig` before building the application to request a specific backend:

```csharp
builder.AddSingleton(new PixelyConfig(GpuBackend: GpuBackend.Direct3D12));
```

`GpuBackend` supports `Automatic`, `Vulkan`, `Direct3D12`, `Metal` and `WebGpu`. An explicit choice is passed to SDL as `vulkan`, `direct3d12`, `metal` or `webgpu` and advertises only that backend's shader format; device creation fails if the requested driver is unavailable. Automatic Windows device creation advertises both SPIR-V and DXIL, allowing SDL to select Vulkan or Direct3D 12; in the browser it advertises WGSL only. Vulkan-specific device options remain enabled whenever Vulkan can be selected. `WebGpu` exists only in the browser, see [Hosting](hosting.md).

Set the `PIXELY_GRAPHICS` environment variable to override `PixelyConfig.GpuBackend` without changing application code:

```shell
PIXELY_GRAPHICS=vulkan dotnet run --project tutorials/Pixely.Tutorials.Triangle
```

Supported values are `automatic`, `vulkan`, `direct3d12`, `metal` and `webgpu`, matched case-insensitively. An unset, empty, or whitespace-only value leaves `PixelyConfig.GpuBackend` in effect. Any other value stops `Build()` with an error that lists the supported values. The other `PixelyConfig` variables are listed in headless.md.

The selected SDL driver is available from `GpuDevice.Driver` for diagnostics.

### Manual Direct3D 12 validation

On a GPU-equipped Windows system, add the following registrations to each application under test:

```csharp
builder.AddSingleton(new PixelyConfig(
    EnableGpuValidation: true,
    GpuBackend: GpuBackend.Direct3D12));

builder.OnBuilt((GpuDevice gpuDevice) =>
{
    if (gpuDevice.Driver != "direct3d12")
    {
        throw new InvalidOperationException($"Expected direct3d12, got {gpuDevice.Driver}");
    }
});
```

Run these representative workloads and confirm that each renders without SDL GPU validation errors:

```shell
dotnet run --project tutorials/Pixely.Tutorials.Triangle
dotnet run --project tutorials/Pixely.Tutorials.ImageLoading
dotnet run --project tutorials/Pixely.Tutorials.StorageBuffer
dotnet run --project tutorials/Pixely.Tutorials.ComputeShader
```

Together these cover basic graphics, texture/sampler bindings, storage buffers, and compute dispatch.

## Vertex Attribute Mapping

Vertex attributes map from vertex buffer types to shader input semantics:

**C# Vertex Type:**
```csharp
.AddVertexBufferConfig<PositionColorVertex>()
```

**Shader Input:**
```csharp
struct Input
{
    float4 Position : TEXCOORD0;
    float4 Color : TEXCOORD1;
};
```

The order of `TEXCOORD` semantics must match the order of fields in the C# vertex struct.

## Multiple Render Targets (MRT)

Fragment shader with multiple outputs for deferred rendering:

```csharp
struct Output
{
    float4 Albedo : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Position : SV_Target2;
};

[shader("fragment")]
Output fragmentMain(VertexToFragment input)
{
    Output output;
    output.Albedo = float4(1.0, 0.0, 0.0, 1.0);
    output.Normal = float4(0.0, 1.0, 0.0, 1.0);
    output.Position = float4(0.0, 0.0, 1.0, 1.0);
    return output;
}
```

**Pipeline configuration must match:**

```csharp
.AddColorTarget(renderContextBuffers.AlbedoBuffer.Format)   // SV_Target0
.AddColorTarget(renderContextBuffers.NormalBuffer.Format)   // SV_Target1
.AddColorTarget(renderContextBuffers.PositionBuffer.Format) // SV_Target2
```

## Metadata Files

For each graphics shader program, the build generates one `.metadata.json` file:

```json
{
  "kind": "Graphics",
  "vertex": {
    "bindingLayout": {},
    "systemValueInputs": {},
    "shaders": [
      {
        "format": "SpirV",
        "filename": "shader.vertex.spv",
        "entryPoint": "vertexMain"
      }
    ]
  },
  "fragment": {
    "bindingLayout": {},
    "shaders": [
      {
        "format": "SpirV",
        "filename": "shader.fragment.spv",
        "entryPoint": "fragmentMain"
      }
    ]
  },
  "sourceHash": "...",
  "sourceDependencies": [],
  "slangVersion": "..."
}
```

Each stage has its own binding layout because resources are reflected for the entry point that uses them. This metadata is used by the loader to validate bindings and create both native GPU shader objects transactionally. You don't need to edit it manually.

Per-target shader records carry the source entry point name for every format. `main` is never a valid entry point name: Metal reserves it, so Slang would rename it in the MSL output and the name would differ between backends.

### WGSL bindings

SDL's WebGPU backend reads the bind group layout out of the WGSL text, and WebGPU has no combined image sampler: a sampled texture and its sampler need two bindings. SDL requires the read-only group's bindings to be dense from 0 with each sampled texture followed by its sampler, then the storage textures, then the storage buffers, all in slot order; read-write and uniform groups use the slot as the binding. Slang emits the register index as the binding, so a texture would collide with its sampler, and no register shift produces the interleave. The compiler rewrites the `@binding` numbers of the generated WGSL from the reflected resources instead, so the register convention above is all a shader needs; the desktop formats are unchanged. A shader whose WGSL declares a resource the reflection does not list, or the other way round, fails the build.

Three limits. The reflection classifies every read-only texture as sampled, so a read-only storage texture (a `Texture2D` read with `Load` and no sampler at its index) fails the sampler pairing check rather than being placed after the samplers. A depth texture sampled as a float texture needs SDL's `//SDLGPU_ForceAllowSamplingForTexture(group, binding)` directive in the WGSL, which nothing emits yet. Comparison sampling (`SamplerComparisonState`, `SampleCmp`) does not work in the browser: Slang emits `texture_2d<f32>` beside `sampler_comparison`, and WGSL's `textureSampleCompare` requires `texture_depth_2d`.

## Notes

- Graphics programs use the fixed source entry points `vertexMain` and `fragmentMain`; compute shaders use `computeMain`
- Shader compilation is cached based on the source hash, Slang version, and expected target formats
- SPIR-V is used by Vulkan, DXIL by Direct3D 12, and MSL by Metal
- Always use explicit register bindings for constant buffers
- Space3 is used for fragment constant buffers by convention
- Uniform data is pushed per-draw call before rendering
