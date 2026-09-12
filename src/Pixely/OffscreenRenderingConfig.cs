namespace Pixely;

/// <summary>Registered by <c>UseOffscreenRendering()</c>; its presence makes every window an <see cref="OffscreenWindow"/>.</summary>
public sealed record OffscreenRenderingConfig(TimeSpan FrameInterval);
