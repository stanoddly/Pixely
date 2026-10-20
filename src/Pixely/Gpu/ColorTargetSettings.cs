namespace Pixely.Gpu;

public class ColorTargetSettings
{
    public LinearColor ClearColorValue { get; init; } = LinearColors.Grey;
    public LoadOperation LoadOperation { get; init; } = LoadOperation.Clear;
    public StoreOperation StoreOperation { get; init; } = StoreOperation.Store;
    public static readonly ColorTargetSettings Clear = new();
}