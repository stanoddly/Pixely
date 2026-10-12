# 3D models

`Pixely.Models` loads binary glTF 2.0 files (`.glb`) and plays their skeletal animations. It uploads meshes and textures to the GPU and computes the matrices to draw them with. It ships no shader: drawing is up to the game. [Pixely.Tutorials.SkinnedModel](../tutorials/Pixely.Tutorials.SkinnedModel/README.md) has a complete renderer with its shaders.

## Loading

`RegisterModelLoading()` registers `IModelLoader`:

```csharp
builder.RegisterModelLoading();

Model model = modelLoader.Load("models/CesiumMan.glb");
```

`Load` reads the file from the `ContentSource`, checks it, and uploads its meshes and base color textures through `GpuMemorySystem`. The uploads run when the frame submits them, like every other upload. The caller owns the result: `Model.Dispose()` releases its buffers and textures. The loader keeps no cache, so loading a path twice gives two models.

`Load` throws `InvalidDataException` for a malformed file and `NotSupportedException` for a glTF feature it does not read (see [Unsupported](#unsupported)). Both name the object and its index, for example `meshes[0].primitives[1]: ...`. A load that throws releases everything it created on the GPU.

Images inside the file are decoded by `IImageDecoder`, which `PixelyAppBuilder` registers as SDL3_image unless the app registers its own. In the browser, which formats decode depends on how the app's `SDL3_image.a` was built ([hosting.md](hosting.md)).

## The model

Every array is in file order, so an index means the same as in the file.

| Member | Content |
| --- | --- |
| `Nodes` | `ModelNode`: name, `Parent`, `Children`, `LocalTransform`, and optional `Mesh` and `Skin` indices |
| `Scenes`, `DefaultScene` | `ModelScene`: the root nodes drawn together. `DefaultScene` is the file's `scene`, or null when the file names none; a viewer usually draws scene 0 then |
| `Meshes` | `ModelMesh`: one `ModelPrimitive` per draw |
| `Materials` | `ModelMaterial`: base color factor and texture, alpha mode and cutoff, double-sided |
| `Skins` | `ModelSkin`: joint node indices, inverse bind matrices, optional skeleton root |
| `Animations` | `ModelAnimation`: channels that animate node translations, rotations and scales |

A `ModelPrimitive` is an indexed triangle list, and every draw starts at vertex and index 0. Primitives that read the same accessors share their buffers:

- `Vertices`: `PositionTextureNormalVertex`, for vertex buffer slot 0. Normals are unit length. A primitive without `TEXCOORD_0` has zero texture coordinates.
- `Skin`: `SkinVertex`, four `ushort` joint indices and four weights that sum to 1, for vertex buffer slot 1. It is null for a primitive without `JOINTS_0`. The joint indices index the skin's `Joints`, not the model's nodes.
- `Indices`: 16 or 32 bits, as the file stores them. 8-bit indices are widened to 16. A primitive without indices gets sequential ones, 16 bits wide up to 65,535 vertices.
- `Material`: an index into `Materials`, or null for the glTF default material, `ModelMaterial.Default`.

A pipeline for static primitives declares one vertex buffer and one for skinned primitives declares two:

```csharp
builder.AddVertexBufferConfig<PositionTextureNormalVertex>();
builder.AddVertexBufferConfig<SkinVertex>();
```

The shader then reads slot 0 at `TEXCOORD0` to `TEXCOORD2` (position, texture coordinates, normal) and slot 1 at `TEXCOORD3` (`uint4` joints) and `TEXCOORD4` (`float4` weights).

### Materials and textures

`ModelMaterial.BaseColorTexture` is a `ModelTexture`: the texture, and the file's sampler settings as a `SamplerConfig`. The game creates the `Sampler` with `GpuDevice.CreateSampler` and owns it. A texture without a sampler in the file gets `Repeat` and `Linear`. Textures have one mip level, so a glTF mipmap filter maps to its base filter.

glTF base color textures are sRGB, so they are uploaded as `R8G8B8A8UnormSrgb` and sampling returns linear values. `BaseColorFactor` is linear too. Pixely's swapchain is not an sRGB format, so a shader that lights in linear space encodes its result to sRGB before writing it. The tutorial's `model_common.slang` shows how.

Materials sharing an image share its texture, and so do images that start at the same byte of the file.

## Poses and animation

A `ModelPose` holds the local transform of every node for one instance of a model. Two characters that share a `Model` use two poses. After construction nothing in it allocates.

```csharp
ModelPose pose = new(model);
Matrix4x4[] world = new Matrix4x4[model.Nodes.Length];
Matrix4x4[] joints = new Matrix4x4[model.Skins[0].Joints.Length];

// Each frame:
ModelAnimation walk = model.Animations[0];
float time = walk.Duration > 0 ? elapsedSeconds % walk.Duration : 0;
pose.Reset();
pose.Apply(walk, time);
pose.ComputeWorldMatrices(world);
ModelPose.ComputeJointMatrices(model.Skins[0], world, joints);
gpuMemorySystem.UpdateStorageBuffer<Matrix4x4>(jointBuffer, joints);
```

- `Reset` returns every node to the transform the file gives it. `Apply` only sets the components the animation animates, so without `Reset` a node keeps what an earlier animation set.
- `Apply` clamps a time outside the animation to its first or last keyframe. Looping is the caller's choice. `Duration` is the last keyframe time over all channels, so it is 0 only when every keyframe is at time 0.
- `LocalTransforms` can be edited directly, for example to turn a head toward a target after `Apply`.
- `STEP`, `LINEAR` and `CUBICSPLINE` interpolation follow the glTF specification. Rotations interpolate along the shorter arc.

## Matrices

All matrices are `System.Numerics.Matrix4x4`, which transforms row vectors: `Vector3.Transform(point, matrix)`.

- `NodeTransform.ToMatrix()` is scale, then rotation, then translation: `CreateScale(S) * CreateFromQuaternion(R) * CreateTranslation(T)`.
- A world matrix is `local * parentWorld`.
- A joint matrix is `inverseBind * world[joint]`. It already holds the whole transform from the mesh to world space: glTF ignores the transform of a skinned mesh's own node, so a shader must not apply it on top.
- glTF writes matrices column-major for column vectors. That is the transpose of a row-vector matrix, so its 16 numbers read in order into `M11` to `M44` give the `Matrix4x4`.

Uploaded as they are, these matrices work with `mul(matrix, vector)` in a `ConstantBuffer<float4x4>`, the same as Pixely's own shaders. In a storage buffer, the tutorial reads them as a struct of four `float4` rows instead, because the layout of a `float4x4` in a storage buffer depends on the compiler and backend.

## Coordinates and winding

glTF is right-handed with +Y up, and a model's front faces +Z. Pixely does not convert coordinates, so the camera must be right-handed too: `Matrix4x4.CreateLookAt` and `CreatePerspectiveFieldOfView`. `Pixely.Utils.PerspectiveCamera` is left-handed and shows the model mirrored.

glTF front faces wind counter-clockwise, while Pixely's pipeline default is clockwise. A transform with a negative determinant mirrors the model and reverses its winding:

- A static draw is mirrored when `world.GetDeterminant() < 0`. Draw it with `FrontFace.Clockwise`, otherwise with `FrontFace.CounterClockwise`.
- A skinned draw has no single world matrix. When every joint matrix has a negative determinant it winds clockwise, when every one is positive counter-clockwise. When the signs differ, its triangles wind both ways and it needs `CullMode.None`. This is a heuristic: blending joints that turn very differently, such as half turns about different axes, can flip a triangle even when every sign agrees. A typical rig blends joints that are close, and an app that must be exact draws with `CullMode.None`.
- A double-sided material needs `CullMode.None` in any case.

## Checks and repairs

Model files are untrusted data. The loader checks everything it uses: references between objects, every accessor against its buffer view, component types for each use, finite numbers, index ranges, attribute counts, the node graph (one parent at most, no cycles), skins against the meshes they deform, and animation keyframes. It is not a full conformance check; the [glTF Validator](https://github.khronos.org/glTF-Validator/) is.

Size arithmetic is checked, and allocations are bounded by the file's size:

- Each accessor is converted once per use, and primitives and channels that read the same accessors share the result. Everything converted together is at most 16 times the file's size, so accessors that alias the same bytes cannot multiply it.
- An image is checked against its `mimeType` and its size is read from its PNG or JPEG header before it is decoded. Each side must be between 1 and 16,384 pixels, so a small file cannot make the decoder allocate more than 1 GiB per image. Whether the GPU accepts a texture that large depends on the device.
- 16-bit indices cannot be 65,535, which graphics APIs can read as a primitive restart. glTF forbids it too.

Some faults are common in exported files and leave the data usable, so the loader repairs them instead of failing:

- Weights that do not sum to exactly 1 are divided by their sum. Negative weights and weights that sum to 0 still fail.
- A joint listed twice in one vertex is kept; blending adds its weights, which gives the same result.
- Normals, node rotations and rotation keyframes that are not unit length are normalized. Zero-length ones still fail. `CUBICSPLINE` tangents are never normalized.

`baseColorFactor` must be between 0 and 1, as glTF requires.

A node `matrix` must decompose into translation, rotation and scale, as glTF requires; a matrix with shear or perspective fails.

## Unsupported

These fail with `NotSupportedException`:

- `.gltf` files, and buffers or images with a `uri`. Only the GLB binary chunk is read.
- Any entry in `extensionsRequired`. Entries only in `extensionsUsed` are ignored.
- Sparse accessors, morph targets and `weights` animation channels.
- Primitives other than triangle lists, and primitives without `POSITION` or `NORMAL`.
- More than one set of joints and weights (`JOINTS_1`).
- A base color texture on a texture coordinate set other than 0, or with a `mimeType` other than `image/png` and `image/jpeg`. Data that does not start like the image type it declares fails with `InvalidDataException`.

These are ignored: metallic-roughness, normal, occlusion and emissive textures and factors, cameras, lights, `extras`, and attributes other than `POSITION`, `NORMAL`, `TEXCOORD_0`, `JOINTS_0` and `WEIGHTS_0`.

## Memory

The returned `Model` keeps nodes, scenes, skins, materials and animation keyframes. The file's bytes, the converted vertex and index arrays and the encoded images are released when `Load` returns. While `Load` runs it holds the file, its parsed JSON, the converted arrays, one decoded image at a time, and the upload transfer buffers. In the browser, the `.glb` also stays in the in-memory file system for the app's lifetime ([content-distribution.md](content-distribution.md)).
