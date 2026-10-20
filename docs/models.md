# 3D models

`Pixely.Models` loads binary glTF 2.0 files (`.glb`) and plays their skeletal animations. It uploads meshes and textures to the GPU and computes the matrices to draw them with. It ships no shader: drawing is up to the game. [Pixely.Tutorials.SkinnedModel](../tutorials/Pixely.Tutorials.SkinnedModel/README.md) has a complete renderer with its shaders.

## Loading

`RegisterModelLoading()` registers `IModelLoader` and `ModelInstanceFactory`:

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
| `Scenes`, `DefaultScene` | `ModelScene`: the root nodes drawn together, and `MeshNodes`, every node with a mesh under them. `DefaultScene` is the file's `scene`, or null when the file names none; a viewer usually draws scene 0 then |
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

glTF base color textures are sRGB, so they are uploaded as `R8G8B8A8UnormSrgb` and sampling returns linear values. `BaseColorFactor` is a `LinearColor`, as glTF defines it. Pixely's swapchain is an sRGB format, so a shader lights in linear light and writes its result as it is; the GPU encodes it to sRGB on write. The tutorial's `model_common.slang` shows this.

Materials sharing an image share its texture, and so do images whose buffer views cover the same bytes.

## Instances

A `ModelInstance` is one character drawn from a shared `Model`. It holds a pose, the world matrix of every node, and the joint matrices of every skin with a storage buffer for each. `ModelInstanceFactory` creates it:

```csharp
ModelInstance instance = modelInstanceFactory.Create(model);

// Each frame, in an update:
instance.Animate(model.Animations[0], frameClock.ElapsedTime);

// When drawing, at a placement such as Matrix4x4.CreateTranslation(position). A file can have no scenes.
ImmutableArray<int> meshNodes = model.Scenes.IsEmpty ? [] : model.Scenes[model.DefaultScene ?? 0].MeshNodes;
foreach (int node in meshNodes)
{
    Matrix4x4 world;
    Winding winding;
    if (model.Nodes[node].Skin is { } skin)
    {
        // The joint matrices place the mesh in the model; the shader applies world after them.
        world = placement;
        winding = instance.GetSkinWinding(skin, placement);
        renderPass.BindVertexStorageBuffer(instance.GetJointBuffer(skin));
    }
    else
    {
        world = instance.WorldMatrices[node] * placement;
        winding = ModelInstance.GetWinding(world);
    }

    // Push world as the shader's world matrix, and bind the pipeline for winding.
}
```

- `Create` computes the matrices in the pose the file gives the model and uploads the joints, so a new instance can be drawn before it is animated.
- `Animate` resets the pose, applies the animation at the elapsed time looped over its `Duration`, and updates the matrices. It throws `ArgumentException` for an animation of another model, before changing the pose. The loop is computed in double, so a long-running game does not lose its place in the loop.
- `UpdateMatrices` computes the matrices from `Pose` as it is and uploads the joints. Use it after editing `Pose` yourself, for example to play an animation once, blend two animations, or turn a head toward a target.
- The game calls `Animate` or `UpdateMatrices` from its own update; the instance does not update itself. Uploads from every instance go into the frame's one copy pass.
- The instance owns its joint buffers, not the model. Dispose every instance, then the model. A disposed instance throws `ObjectDisposedException` from `Animate`, `UpdateMatrices` and `GetJointBuffer`.

## Poses and animation

A `ModelPose` holds the local transform of every node for one instance of a model. `ModelInstance.Pose` is one; a game that keeps its own matrices uses a pose directly. Two characters that share a `Model` use two poses. After construction nothing in it allocates.

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
- `STEP`, `LINEAR` and `CUBICSPLINE` interpolation follow the glTF specification. Rotations interpolate along the shorter arc. Where a `CUBICSPLINE` result has no direction or overflows, such as halfway between two keys of opposite sign with zero tangents, the nearer keyframe's value is used.
- `Apply` throws `ArgumentException` for an animation of another model, because its channels index that model's nodes.

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

`ModelInstance.GetWinding(world)` and `instance.GetSkinWinding(skin, placement)` apply these rules and return a `Winding`: `CounterClockwise`, `Clockwise`, or `Mixed` for `CullMode.None`. The placement is the transform a skinned shader applies after the joints, to draw one instance at several places.

## Checks and repairs

Model files are untrusted data. The loader checks everything it uses: references between objects, every accessor against its buffer view, component types for each use, finite numbers, index ranges, attribute counts, the node graph (one parent at most, no cycles), skins against the meshes they deform, and animation keyframes. It is not a full conformance check; the [glTF Validator](https://github.khronos.org/glTF-Validator/) is.

Size arithmetic is checked, and allocations are bounded before they happen:

- Each accessor is converted once per use, and primitives and channels that read the same accessors share the result. Everything converted together, the accessors, the vertex, skin and index arrays built from them, the skins' inverse bind matrices and the scenes' mesh nodes, is at most 16 times the file's size, so accessors that alias the same bytes, or scenes that share one hierarchy, cannot multiply it.
- An image is checked against its `mimeType` and its size is read from its PNG or JPEG header before it is decoded. Each side must be between 1 and 16,384 pixels, and the images of a model together at most 16,384 × 16,384 pixels, which decode to 1 GiB. A compressed image can be much smaller than its pixels, so a small file can still use that much: while the largest image uploads, its decoded pixels, its transfer buffer and its texture each take up to 1 GiB. Whether the GPU accepts a texture that large depends on the device.
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

The returned `Model` keeps nodes, scenes, skins, materials and animation keyframes. The file's bytes, the converted vertex and index arrays and the encoded images are released when `Load` returns. A `ModelInstance` holds one local transform and one world matrix per node, and one joint matrix per joint of each skin, which it also keeps in a storage buffer. While `Load` runs it holds the file, its parsed JSON, the converted arrays, one decoded image at a time, and the upload transfer buffers. In the browser, the `.glb` also stays in the in-memory file system for the app's lifetime ([content-distribution.md](content-distribution.md)).
