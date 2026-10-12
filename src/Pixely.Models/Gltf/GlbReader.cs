using System.Buffers.Binary;

namespace Pixely.Models.Gltf;

// Splits a GLB container into its JSON chunk and its optional binary chunk (glTF 2.0, section 4.4).
internal static class GlbReader
{
    private const uint Magic = 0x46546C67;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinaryChunkType = 0x004E4942;
    private const int HeaderLength = 12;
    private const int ChunkHeaderLength = 8;

    public static GlbChunks Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < HeaderLength)
        {
            throw new InvalidDataException($"The file has {span.Length} bytes, fewer than the {HeaderLength}-byte GLB header.");
        }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(span);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);

        if (magic != Magic)
        {
            throw new InvalidDataException("The file does not start with the GLB magic 'glTF'.");
        }

        if (version != 2)
        {
            throw new InvalidDataException($"The GLB container version is {version}; only version 2 is supported.");
        }

        if (length != span.Length)
        {
            throw new InvalidDataException($"The GLB header declares {length} bytes, but the file has {span.Length}.");
        }

        ReadOnlyMemory<byte>? json = null;
        ReadOnlyMemory<byte>? binary = null;
        int chunkIndex = 0;
        int offset = HeaderLength;

        while (offset < span.Length)
        {
            if (span.Length - offset < ChunkHeaderLength)
            {
                throw new InvalidDataException($"GLB chunk {chunkIndex} has a truncated header.");
            }

            uint chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            uint chunkType = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            int chunkStart = offset + ChunkHeaderLength;

            if (chunkLength > (uint)(span.Length - chunkStart))
            {
                throw new InvalidDataException($"GLB chunk {chunkIndex} declares {chunkLength} bytes, more than the file holds.");
            }

            if (chunkLength % 4 != 0)
            {
                throw new InvalidDataException($"GLB chunk {chunkIndex} is {chunkLength} bytes long, which is not a multiple of 4.");
            }

            ReadOnlyMemory<byte> chunk = data.Slice(chunkStart, (int)chunkLength);

            if (chunkIndex == 0)
            {
                if (chunkType != JsonChunkType)
                {
                    throw new InvalidDataException("The first GLB chunk is not the JSON chunk.");
                }

                json = chunk;
            }
            else if (chunkType == BinaryChunkType)
            {
                if (chunkIndex != 1)
                {
                    throw new InvalidDataException($"GLB chunk {chunkIndex} is a binary chunk; the binary chunk must be the second chunk.");
                }

                binary = chunk;
            }
            else if (chunkType == JsonChunkType)
            {
                throw new InvalidDataException($"GLB chunk {chunkIndex} is a second JSON chunk.");
            }

            // Chunks of other types are skipped, as the specification requires.
            offset = chunkStart + (int)chunkLength;
            chunkIndex++;
        }

        if (json is null)
        {
            throw new InvalidDataException("The GLB file has no JSON chunk.");
        }

        return new GlbChunks(json.Value, binary);
    }
}

internal readonly record struct GlbChunks(ReadOnlyMemory<byte> Json, ReadOnlyMemory<byte>? Binary);
