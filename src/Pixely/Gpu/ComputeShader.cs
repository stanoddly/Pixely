using Pixely.ShaderCommon;
using Pixely.Shaders;

namespace Pixely.Gpu;

public class ComputeShader
{
    public ShaderBindingLayout BindingLayout { get; }
    public uint ThreadCountX { get; }
    public uint ThreadCountY { get; }
    public uint ThreadCountZ { get; }
    internal byte[] Code { get; }
    internal string EntryPoint { get; }
    internal ShaderFormat Format { get; }

    internal ComputeShader(byte[] code, string entryPoint, ShaderFormat format, ShaderBindingLayout bindingLayout, uint threadCountX, uint threadCountY, uint threadCountZ)
    {
        Code = code;
        EntryPoint = entryPoint;
        Format = format;
        BindingLayout = bindingLayout;
        ThreadCountX = threadCountX;
        ThreadCountY = threadCountY;
        ThreadCountZ = threadCountZ;
    }
}
