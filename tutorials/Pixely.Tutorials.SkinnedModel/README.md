# Skinned model

This tutorial loads `Content/models/CesiumMan.glb` with `IModelLoader`, plays its walk animation on a `ModelInstance`, and draws it with its own shaders. Pixely loads the model; drawing it is up to the game, so the shaders here are an example, not part of Pixely. See [docs/models.md](../../docs/models.md) for the API.

```shell
dotnet run --project tutorials/Pixely.Tutorials.SkinnedModel
```

The model is drawn three times, side by side:

| Copy | What it shows | What it should look like |
| --- | --- | --- |
| Left | Mirrored on X | The middle copy's mirror image, including the logo on the head, with the same lighting. No faces are missing, because its placement has a negative determinant and it draws with clockwise front faces. |
| Middle | As loaded | The man walking in place, facing the camera, lit from the upper right. |
| Right | Drawn as if double-sided | Identical to the middle copy. The mesh is closed, so turning culling off shows nothing new; the copy only exercises that path. |

The close camera shows the side copies from 18 degrees off their axis, so they look turned compared to the middle one; a distant camera shows them identical.

## How it draws

- `AnimatedModel` calls `ModelInstance.Animate` every frame with `FrameClock.ElapsedTime`, which updates the pose, the world and joint matrices, and the joint storage buffer. The three copies share one instance.
- `ModelRenderer` draws every node with a mesh in the default scene. A node with a skin uses `model_skinned.slang`, which takes the vertex's four joints and weights from vertex buffer slot 1 and blends their matrices. A node without one uses `model_static.slang` with the node's world matrix. Each copy's placement applies after the node's world matrix or the joints.
- Each program has three pipelines: back-face culling with counter-clockwise front faces, the same with clockwise front faces for mirrored transforms, and no culling for double-sided materials. `ModelInstance.GetWinding` picks a static draw's winding from the sign of its world matrix's determinant. A skinned draw has no single world matrix, so `GetSkinWinding` uses the signs of its joint matrices' determinants times its placement's, and the draw turns culling off when they differ. The signs are a heuristic that fits a typical rig ([models.md](../../docs/models.md#coordinates-and-winding)).
- glTF is right-handed with +Y up, so the camera uses `Matrix4x4.CreateLookAt` and `CreatePerspectiveFieldOfView`. `Pixely.Utils.PerspectiveCamera` is left-handed and would mirror the model.
- Base color textures are sRGB, so sampling returns linear values. The swapchain is not an sRGB format, so the fragment shader encodes its result to sRGB itself; it skips that when the swapchain format is sRGB.
- Skinned normals use the cofactor matrix of the blended joint matrix, which keeps them correct under non-uniform scale and mirroring. The fragment shader turns each normal toward the camera before lighting, so the back of a double-sided face is lit too.

## Not covered

- Alpha blending: `AlphaMode.Blend` materials are drawn opaque. `AlphaMode.Mask` discards below the cutoff.
- Metallic-roughness, normal, occlusion and emissive maps, which the loader does not read.

## Credits

Cesium Man is © 2017, Cesium, under CC-BY 4.0. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
