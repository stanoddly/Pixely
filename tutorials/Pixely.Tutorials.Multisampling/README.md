# Multisampling

This tutorial draws a thin, tilted triangle with four samples per pixel (MSAA) and resolves the result into the swapchain texture.

- The color target and the depth buffer are created with `SampleCount.Count4`. Every attachment of a pass has the same sample count.
- The pipeline is built with `EnableMultiSampling(SampleCount.Count4)`, the attachments' sample count.
- `AddColorTarget(colorTarget, swapchainTexture, settings)` names the swapchain texture as the resolve texture, and the settings' `StoreOperation.Resolve` resolves into it.
- The targets are created again when the swapchain texture's size or format changes, because a resolve texture has the size and format of its target.

See `docs/render-pass-flow.md` for the rules.
