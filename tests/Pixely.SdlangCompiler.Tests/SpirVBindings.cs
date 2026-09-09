using System.Buffers.Binary;
using System.Text;

namespace Pixely.SdlangCompiler.Tests;

internal readonly record struct SpirVBinding(int DescriptorSet, int Binding);

/// <summary>Reads the descriptor set and binding SPIR-V assigns to each named shader parameter.</summary>
internal static class SpirVBindings
{
    private const int HeaderWordCount = 5;
    private const int OpName = 5;
    private const int OpDecorate = 71;
    private const int DecorationBinding = 33;
    private const int DecorationDescriptorSet = 34;

    public static Dictionary<string, SpirVBinding> Read(string spirVPath)
    {
        byte[] bytes = File.ReadAllBytes(spirVPath);
        uint[] words = new uint[bytes.Length / sizeof(uint)];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint)));
        }

        Dictionary<uint, string> namesById = new();
        Dictionary<uint, int> setsById = new();
        Dictionary<uint, int> bindingsById = new();

        int word = HeaderWordCount;
        while (word < words.Length)
        {
            int wordCount = (int)(words[word] >> 16);
            int opCode = (int)(words[word] & 0xFFFF);

            if (opCode == OpName)
            {
                namesById[words[word + 1]] = ReadString(words, word + 2, wordCount - 2);
            }
            else if (opCode == OpDecorate && wordCount == 4)
            {
                uint target = words[word + 1];
                int value = (int)words[word + 3];
                switch ((int)words[word + 2])
                {
                    case DecorationDescriptorSet:
                        setsById[target] = value;
                        break;
                    case DecorationBinding:
                        bindingsById[target] = value;
                        break;
                }
            }

            word += wordCount;
        }

        return setsById
            .Where(set => bindingsById.ContainsKey(set.Key) && namesById.ContainsKey(set.Key))
            .ToDictionary(set => namesById[set.Key], set => new SpirVBinding(set.Value, bindingsById[set.Key]));
    }

    private static string ReadString(uint[] words, int firstWord, int wordCount)
    {
        byte[] literal = new byte[wordCount * sizeof(uint)];
        for (int i = 0; i < wordCount; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(literal.AsSpan(i * sizeof(uint)), words[firstWord + i]);
        }

        return Encoding.UTF8.GetString(literal).TrimEnd('\0');
    }
}
