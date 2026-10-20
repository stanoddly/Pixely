using System.Collections.Immutable;
using System.Numerics;
using Pixely.Gpu;

namespace Pixely.Models.Gltf;

// Reads materials and the images they use.
internal static class GltfMaterialReader
{
    public static ImmutableArray<MaterialData> ReadMaterials(GltfRootDto root)
    {
        GltfMaterialDto[] materialDtos = root.Materials ?? [];
        GltfTextureDto[] textures = root.Textures ?? [];
        GltfSamplerDto[] samplers = root.Samplers ?? [];
        int imageCount = root.Images?.Length ?? 0;
        ImmutableArray<MaterialData>.Builder materials = ImmutableArray.CreateBuilder<MaterialData>(materialDtos.Length);

        for (int index = 0; index < materialDtos.Length; index++)
        {
            GltfMaterialDto material = materialDtos[index];
            string name = $"materials[{index}]";

            LinearColor baseColorFactor = new(1, 1, 1, 1);
            if (material.PbrMetallicRoughness?.BaseColorFactor is { } factor)
            {
                if (factor.Length != 4 || !factor.All(value => value is >= 0 and <= 1))
                {
                    throw new InvalidDataException($"{name}: baseColorFactor must be 4 numbers between 0 and 1.");
                }

                baseColorFactor = new LinearColor(factor[0], factor[1], factor[2], factor[3]);
            }

            int? baseColorImage = null;
            SamplerConfig sampler = GltfSamplers.Default;
            if (material.PbrMetallicRoughness?.BaseColorTexture is { } textureInfo)
            {
                int textureIndex = textureInfo.Index ?? throw new InvalidDataException($"{name}: baseColorTexture.index is missing.");
                if (textureIndex < 0 || textureIndex >= textures.Length)
                {
                    throw new InvalidDataException($"{name}: texture {textureIndex} is out of range.");
                }

                if ((textureInfo.TexCoord ?? 0) != 0)
                {
                    throw new NotSupportedException($"{name}: baseColorTexture uses TEXCOORD_{textureInfo.TexCoord}; only TEXCOORD_0 is supported.");
                }

                GltfTextureDto texture = textures[textureIndex];
                int source = texture.Source ?? throw new NotSupportedException($"textures[{textureIndex}] has no source; textures defined only by extensions are not supported.");
                if (source < 0 || source >= imageCount)
                {
                    throw new InvalidDataException($"textures[{textureIndex}]: source {source} is out of range.");
                }

                if (texture.Sampler is { } samplerIndex)
                {
                    if (samplerIndex < 0 || samplerIndex >= samplers.Length)
                    {
                        throw new InvalidDataException($"textures[{textureIndex}]: sampler {samplerIndex} is out of range.");
                    }

                    sampler = GltfSamplers.ToSamplerConfig(samplers[samplerIndex], $"samplers[{samplerIndex}]");
                }

                baseColorImage = source;
            }

            AlphaMode alphaMode = material.AlphaMode switch
            {
                null or "OPAQUE" => AlphaMode.Opaque,
                "MASK" => AlphaMode.Mask,
                "BLEND" => AlphaMode.Blend,
                _ => throw new InvalidDataException($"{name}: alphaMode '{material.AlphaMode}' is not OPAQUE, MASK or BLEND.")
            };

            float alphaCutoff = material.AlphaCutoff ?? 0.5f;
            if (!float.IsFinite(alphaCutoff) || alphaCutoff < 0)
            {
                throw new InvalidDataException($"{name}: alphaCutoff {alphaCutoff} is out of range.");
            }

            materials.Add(new MaterialData(material.Name, baseColorFactor, baseColorImage, sampler, alphaMode, alphaCutoff, material.DoubleSided ?? false));
        }

        return materials.MoveToImmutable();
    }

    // Only images a material uses are read, so an unused image the loader cannot read, such as an external normal map, does not
    // fail the load. Unused entries stay empty. Images with the same buffer view bytes compare equal, and the loader decodes them
    // once, so their pixels count once toward the total.
    public static ImmutableArray<ReadOnlyMemory<byte>> ReadImages(GltfRootDto root, ImmutableArray<MaterialData> materials, AccessorReader accessors)
    {
        GltfImageDto[] imageDtos = root.Images ?? [];
        ReadOnlyMemory<byte>[] images = new ReadOnlyMemory<byte>[imageDtos.Length];
        HashSet<ReadOnlyMemory<byte>> counted = new();
        long totalPixels = 0;

        foreach (MaterialData material in materials)
        {
            if (material.BaseColorImage is not { } index || !images[index].IsEmpty)
            {
                continue;
            }

            GltfImageDto image = imageDtos[index];
            if (image.Uri is not null)
            {
                throw new NotSupportedException($"images[{index}] has a uri; external and data URI images are not supported.");
            }

            int bufferView = image.BufferView ?? throw new InvalidDataException($"images[{index}] has neither a uri nor a bufferView.");
            if (image.MimeType is not ("image/png" or "image/jpeg"))
            {
                throw new NotSupportedException($"images[{index}]: mimeType '{image.MimeType}' is not supported; only image/png and image/jpeg are.");
            }

            ReadOnlyMemory<byte> data = accessors.GetBufferView(bufferView, out _);
            (int width, int height) = ImageHeader.Check(data.Span, image.MimeType, $"images[{index}]");
            if (counted.Add(data))
            {
                totalPixels += (long)width * height;
                if (totalPixels > ImageHeader.MaxTotalPixels)
                {
                    throw new InvalidDataException($"images[{index}]: the images together have more than {ImageHeader.MaxTotalPixels} pixels.");
                }
            }

            images[index] = data;
        }

        return [.. images];
    }
}
