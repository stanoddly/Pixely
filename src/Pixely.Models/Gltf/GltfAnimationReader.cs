using System.Collections.Immutable;
using System.Numerics;

namespace Pixely.Models.Gltf;

// Reads animations and checks their keyframes.
internal static class GltfAnimationReader
{
    public static ImmutableArray<ModelAnimation> ReadAnimations(GltfAnimationDto[] animationDtos, GltfNodeDto[] nodes, ImmutableArray<ModelNode> modelNodes, AccessorReader accessors)
    {
        ImmutableArray<ModelAnimation>.Builder animations = ImmutableArray.CreateBuilder<ModelAnimation>(animationDtos.Length);
        // Channels that read the same accessors share their checked keyframes, so each accessor is checked and repaired once.
        Dictionary<int, float[]> times = new();
        Dictionary<(int Output, AnimationPath Path, bool Cubic), float[]> outputs = new();

        for (int animationIndex = 0; animationIndex < animationDtos.Length; animationIndex++)
        {
            GltfAnimationDto animation = animationDtos[animationIndex];
            string animationName = $"animations[{animationIndex}]";
            GltfAnimationChannelDto[] channelDtos = animation.Channels ?? [];
            GltfAnimationSamplerDto[] samplers = animation.Samplers ?? [];
            HashSet<(int Node, AnimationPath Path)> targets = new();
            ImmutableArray<AnimationChannel>.Builder channels = ImmutableArray.CreateBuilder<AnimationChannel>();

            for (int channelIndex = 0; channelIndex < channelDtos.Length; channelIndex++)
            {
                GltfAnimationChannelDto channel = channelDtos[channelIndex];
                string name = $"{animationName}.channels[{channelIndex}]";
                GltfAnimationTargetDto target = channel.Target ?? throw new InvalidDataException($"{name}: target is missing.");

                // A target without a node is valid glTF, for extensions to animate other properties; there is nothing to animate here.
                if (target.Node is not { } node)
                {
                    continue;
                }

                if (node < 0 || node >= nodes.Length)
                {
                    throw new InvalidDataException($"{name}: node {node} is out of range.");
                }

                AnimationPath path = target.Path switch
                {
                    "translation" => AnimationPath.Translation,
                    "rotation" => AnimationPath.Rotation,
                    "scale" => AnimationPath.Scale,
                    "weights" => throw new NotSupportedException($"{name}: morph target weights are not supported."),
                    _ => throw new InvalidDataException($"{name}: path '{target.Path}' is not translation, rotation, scale or weights.")
                };

                if (nodes[node].Matrix is not null)
                {
                    throw new InvalidDataException($"{name}: node {node} has a matrix, which cannot be animated.");
                }

                if (!targets.Add((node, path)))
                {
                    throw new InvalidDataException($"{name}: another channel of the animation already targets the {target.Path} of node {node}.");
                }

                int samplerIndex = channel.Sampler ?? throw new InvalidDataException($"{name}: sampler is missing.");
                if (samplerIndex < 0 || samplerIndex >= samplers.Length)
                {
                    throw new InvalidDataException($"{name}: sampler {samplerIndex} is out of range.");
                }

                channels.Add(ReadChannel(samplers[samplerIndex], $"{animationName}.samplers[{samplerIndex}]", node, path, accessors, times, outputs));
            }

            animations.Add(new ModelAnimation(animation.Name, channels.ToImmutable(), modelNodes));
        }

        return animations.MoveToImmutable();
    }

    private static AnimationChannel ReadChannel(GltfAnimationSamplerDto sampler, string name, int node, AnimationPath path, AccessorReader accessors,
        Dictionary<int, float[]> timesByInput, Dictionary<(int Output, AnimationPath Path, bool Cubic), float[]> outputs)
    {
        AnimationInterpolation interpolation = sampler.Interpolation switch
        {
            null or "LINEAR" => AnimationInterpolation.Linear,
            "STEP" => AnimationInterpolation.Step,
            "CUBICSPLINE" => AnimationInterpolation.CubicSpline,
            _ => throw new InvalidDataException($"{name}: interpolation '{sampler.Interpolation}' is not LINEAR, STEP or CUBICSPLINE.")
        };

        int input = sampler.Input ?? throw new InvalidDataException($"{name}: input is missing.");
        int output = sampler.Output ?? throw new InvalidDataException($"{name}: output is missing.");

        if (!timesByInput.TryGetValue(input, out float[]? times))
        {
            times = ReadTimes(input, name, accessors);
            timesByInput.Add(input, times);
        }

        bool cubic = interpolation == AnimationInterpolation.CubicSpline;
        if (cubic && times.Length < 2)
        {
            throw new InvalidDataException($"{name}: CUBICSPLINE needs at least 2 keyframes.");
        }

        if (!outputs.TryGetValue((output, path, cubic), out float[]? values))
        {
            values = ReadOutput(output, name, path, cubic, accessors);
            outputs.Add((output, path, cubic), values);
        }

        int componentCount = path == AnimationPath.Rotation ? 4 : 3;
        int expected = times.Length * (cubic ? 3 : 1);
        if (values.Length / componentCount != expected)
        {
            throw new InvalidDataException($"{name}: output has {values.Length / componentCount} elements, but {times.Length} keyframes need {expected}.");
        }

        return new AnimationChannel(node, path, interpolation, times, values);
    }

    private static float[] ReadTimes(int input, string name, AccessorReader accessors)
    {
        float[] times = accessors.ReadFloats(input, AccessorUse.AnimationInput);
        if (times[0] < 0)
        {
            throw new InvalidDataException($"{name}: the first keyframe time {times[0]} is negative.");
        }

        for (int i = 1; i < times.Length; i++)
        {
            if (times[i] <= times[i - 1])
            {
                throw new InvalidDataException($"{name}: keyframe times are not strictly increasing at keyframe {i}.");
            }
        }

        return times;
    }

    private static float[] ReadOutput(int output, string name, AnimationPath path, bool cubic, AccessorReader accessors)
    {
        if (path != AnimationPath.Rotation)
        {
            return accessors.ReadFloats(output, AccessorUse.TranslationOrScaleOutput);
        }

        // Key values are unit quaternions, repaired if an exporter left them slightly off. CUBICSPLINE tangents, the first and
        // third element of each keyframe, are derivatives and are never normalized. The repair writes a copy, because the
        // accessor reader shares its arrays.
        float[] source = accessors.ReadFloats(output, AccessorUse.RotationOutput);
        accessors.Charge(source.Length * (long)sizeof(float), name);
        float[] values = (float[])source.Clone();
        for (int element = 0; element < values.Length / 4; element++)
        {
            if (cubic && element % 3 != 1)
            {
                continue;
            }

            int offset = element * 4;
            Quaternion rotation = GltfValues.NormalizeRotation(new Vector4(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]), name);
            values[offset] = rotation.X;
            values[offset + 1] = rotation.Y;
            values[offset + 2] = rotation.Z;
            values[offset + 3] = rotation.W;
        }

        return values;
    }
}
