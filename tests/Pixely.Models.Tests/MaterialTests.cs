using System.Numerics;
using System.Text.Json.Nodes;
using Pixely.Gpu;
using Pixely.Models.Gltf;

namespace Pixely.Models.Tests;

public class MaterialTests
{
    [Test]
    public void Read_MaterialDefaults_MatchGltf()
    {
        GlbBuilder builder = new();
        builder.Add("materials", new JsonObject());

        MaterialData material = builder.Read().Materials[0];

        Assert.That(material.BaseColorFactor, Is.EqualTo(Vector4.One));
        Assert.That(material.BaseColorImage, Is.Null);
        Assert.That(material.AlphaMode, Is.EqualTo(AlphaMode.Opaque));
        Assert.That(material.AlphaCutoff, Is.EqualTo(0.5f));
        Assert.That(material.DoubleSided, Is.False);
    }

    [Test]
    public void Read_Material_ReadsBaseColorAlphaAndSides()
    {
        GlbBuilder builder = new();
        builder.Add("materials", new JsonObject
        {
            ["pbrMetallicRoughness"] = new JsonObject { ["baseColorFactor"] = new JsonArray(0.5, 0.25, 1, 0.75) },
            ["alphaMode"] = "MASK",
            ["alphaCutoff"] = 0.3,
            ["doubleSided"] = true
        });

        MaterialData material = builder.Read().Materials[0];

        Assert.That(material.BaseColorFactor, Is.EqualTo(new Vector4(0.5f, 0.25f, 1, 0.75f)));
        Assert.That(material.AlphaMode, Is.EqualTo(AlphaMode.Mask));
        Assert.That(material.AlphaCutoff, Is.EqualTo(0.3f));
        Assert.That(material.DoubleSided, Is.True);
    }

    [Test]
    public void Read_InvalidAlphaMode_Throws()
    {
        GlbBuilder builder = new();
        builder.Add("materials", new JsonObject { ["alphaMode"] = "CUTOUT" });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_BaseColorTexture_ReadsItsImageAndSampler()
    {
        GlbBuilder builder = AddTexturedMaterial(new JsonObject { ["magFilter"] = 9728, ["minFilter"] = 9987, ["wrapS"] = 33071, ["wrapT"] = 33648 });

        ModelData data = builder.Read();
        MaterialData material = data.Materials[0];

        Assert.That(material.BaseColorImage, Is.EqualTo(0));
        Assert.That(data.Images[0].ToArray(), Is.EqualTo(ImageHeaders.Png(2, 2)));
        Assert.That(material.BaseColorSampler.MagFilter, Is.EqualTo(TextureFilter.Nearest));
        Assert.That(material.BaseColorSampler.MinFilter, Is.EqualTo(TextureFilter.Linear));
        Assert.That(material.BaseColorSampler.AddressModeU, Is.EqualTo(SamplerAddressMode.ClampToEdge));
        Assert.That(material.BaseColorSampler.AddressModeV, Is.EqualTo(SamplerAddressMode.MirroredRepeat));
    }

    [Test]
    public void Read_TextureWithoutSampler_RepeatsAndFiltersLinearly()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null);

        SamplerConfig sampler = builder.Read().Materials[0].BaseColorSampler;

        Assert.That((sampler.MinFilter, sampler.MagFilter), Is.EqualTo((TextureFilter.Linear, TextureFilter.Linear)));
        Assert.That((sampler.AddressModeU, sampler.AddressModeV), Is.EqualTo((SamplerAddressMode.Repeat, SamplerAddressMode.Repeat)));
    }

    [TestCase(9984, TextureFilter.Nearest)]
    [TestCase(9985, TextureFilter.Linear)]
    [TestCase(9986, TextureFilter.Nearest)]
    [TestCase(9729, TextureFilter.Linear)]
    public void Read_MipmapMinFilter_UsesItsBaseFilter(int minFilter, TextureFilter expected)
    {
        GlbBuilder builder = AddTexturedMaterial(new JsonObject { ["minFilter"] = minFilter });

        Assert.That(builder.Read().Materials[0].BaseColorSampler.MinFilter, Is.EqualTo(expected));
    }

    [TestCase("magFilter", 9984)]
    [TestCase("minFilter", 1)]
    [TestCase("wrapS", 1)]
    public void Read_InvalidSamplerValue_Throws(string property, int value)
    {
        GlbBuilder builder = AddTexturedMaterial(new JsonObject { [property] = value });

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_SecondTexCoordSet_IsNotSupported()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null);
        builder.Json["materials"]![0]!["pbrMetallicRoughness"]!["baseColorTexture"]!["texCoord"] = 1;

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_ImageWithUri_IsNotSupported()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null);
        builder.Json["images"]![0] = new JsonObject { ["uri"] = "texture.png" };

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_UnusedImageWithUri_IsIgnored()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null);
        builder.Add("images", new JsonObject { ["uri"] = "normals.png" });

        Assert.That(builder.Read().Images[1].IsEmpty, Is.True);
    }

    [Test]
    public void Read_ImageOfUnsupportedType_IsNotSupported()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null);
        builder.Json["images"]![0]!["mimeType"] = "image/ktx2";

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_TextureWithoutSource_IsNotSupported()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null);
        ((JsonObject)builder.Json["textures"]![0]!).Remove("source");

        Assert.Throws<NotSupportedException>(() => builder.Read());
    }

    [Test]
    public void Read_TextureOutOfRange_Throws()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null);
        builder.Json["materials"]![0]!["pbrMetallicRoughness"]!["baseColorTexture"]!["index"] = 3;

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_BaseColorFactorOutsideZeroToOne_Throws()
    {
        foreach (float value in new[] { -0.1f, 1.1f })
        {
            GlbBuilder builder = new();
            builder.Add("materials", new JsonObject { ["pbrMetallicRoughness"] = new JsonObject { ["baseColorFactor"] = new JsonArray(1, value, 1, 1) } });

            Assert.Throws<InvalidDataException>(() => builder.Read(), $"{value}");
        }
    }

    [Test]
    public void Read_JpegImage_IsRead()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null, ImageHeaders.Jpeg(640, 480), "image/jpeg");

        Assert.That(builder.Read().Images[0].ToArray(), Is.EqualTo(ImageHeaders.Jpeg(640, 480)));
    }

    [TestCase("image/png")]
    [TestCase("image/jpeg")]
    public void Read_ImageNotOfItsMimeType_Throws(string mimeType)
    {
        byte[] image = mimeType == "image/png" ? ImageHeaders.Jpeg(2, 2) : ImageHeaders.Png(2, 2);
        GlbBuilder builder = AddTexturedMaterial(sampler: null, image, mimeType);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [TestCase(16385u, 1u)]
    [TestCase(1u, 16385u)]
    [TestCase(0u, 1u)]
    [TestCase(uint.MaxValue, 1u)]
    public void Read_PngOfUnsupportedSize_ThrowsBeforeDecoding(uint width, uint height)
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null, ImageHeaders.Png(width, height), "image/png");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_JpegLargerThanTheLimit_ThrowsBeforeDecoding()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null, ImageHeaders.Jpeg(16385, 1), "image/jpeg");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_JpegWithoutFrameHeader_Throws()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null, [0xFF, 0xD8, 0xFF, 0xDA, 0, 2], "image/jpeg");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_ImageAtTheSideLimit_IsRead()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null, ImageHeaders.Png(16384, 16384), "image/png");

        Assert.That(builder.Read().Images[0].IsEmpty, Is.False);
    }

    [Test]
    public void Read_ImagesTogetherLargerThanTheLimit_Throws()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null, ImageHeaders.Png(16384, 16384), "image/png");
        AddTexturedMaterial(builder, builder.AddBufferView(ImageHeaders.Png(1, 1)), "image/png");

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    [Test]
    public void Read_ImagesWithTheSameBufferView_ShareOneEntryAndCountOnce()
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null, ImageHeaders.Png(16384, 16384), "image/png");
        AddTexturedMaterial(builder, 0, "image/png");

        ModelData data = builder.Read();

        Assert.That(data.Images[1], Is.EqualTo(data.Images[0]));
    }

    [TestCase(20, "image/png")]
    [TestCase(24, "image/jpeg")]
    public void Read_ImageViewOverTheStartOfAnother_IsCheckedOnItsOwn(int byteLength, string mimeType)
    {
        GlbBuilder builder = AddTexturedMaterial(sampler: null);
        int view = builder.Add("bufferViews", new JsonObject { ["buffer"] = 0, ["byteOffset"] = 0, ["byteLength"] = byteLength });
        AddTexturedMaterial(builder, view, mimeType);

        Assert.Throws<InvalidDataException>(() => builder.Read());
    }

    // Adds an image on the buffer view, a texture and a material that uses it.
    private static void AddTexturedMaterial(GlbBuilder builder, int bufferView, string mimeType)
    {
        int image = builder.Add("images", new JsonObject { ["bufferView"] = bufferView, ["mimeType"] = mimeType });
        int texture = builder.Add("textures", new JsonObject { ["source"] = image });
        builder.Add("materials", new JsonObject { ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = texture } } });
    }

    private static GlbBuilder AddTexturedMaterial(JsonObject? sampler, byte[]? image = null, string mimeType = "image/png")
    {
        GlbBuilder builder = new();
        int view = builder.AddBufferView(image ?? ImageHeaders.Png(2, 2));
        builder.Add("images", new JsonObject { ["bufferView"] = view, ["mimeType"] = mimeType });
        JsonObject texture = new() { ["source"] = 0 };
        if (sampler is not null)
        {
            texture["sampler"] = builder.Add("samplers", sampler);
        }

        builder.Add("textures", texture);
        builder.Add("materials", new JsonObject { ["pbrMetallicRoughness"] = new JsonObject { ["baseColorTexture"] = new JsonObject { ["index"] = 0 } } });
        return builder;
    }
}
