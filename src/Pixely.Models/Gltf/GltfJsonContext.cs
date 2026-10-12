using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pixely.Models.Gltf;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Disallow)]
[JsonSerializable(typeof(GltfRootDto))]
internal partial class GltfJsonContext : JsonSerializerContext;
