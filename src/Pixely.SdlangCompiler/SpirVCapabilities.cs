using System.Buffers.Binary;

namespace Pixely.SdlangCompiler;

/// <summary>Reads the capabilities a SPIR-V module declares.</summary>
internal static class SpirVCapabilities
{
    public const int DrawParameters = 4427;

    private const int HeaderWordCount = 5;
    private const int OpCapability = 17;

    public static HashSet<int> Read(string spirVPath)
    {
        byte[] bytes = File.ReadAllBytes(spirVPath);
        HashSet<int> capabilities = new();

        int word = HeaderWordCount;
        while (word * sizeof(uint) < bytes.Length)
        {
            uint instruction = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * sizeof(uint)));
            int wordCount = (int)(instruction >> 16);
            if ((instruction & 0xFFFF) == OpCapability)
            {
                capabilities.Add((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((word + 1) * sizeof(uint))));
            }

            word += wordCount;
        }

        return capabilities;
    }
}
