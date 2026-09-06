using Pixely.Gpu;

namespace Pixely.Ui;

/// <summary>
/// A run of consecutive instructions sharing a texture and a clip, drawn with one sampler binding
/// and one scissor setting.
/// </summary>
internal readonly record struct PaintBatch(int Start, int Count, Texture? Texture, Rectangle Clip);

/// <summary>
/// Groups the ordered instruction list into runs. Because the whole UI uses a single pipeline,
/// grouping never has to break for a pipeline change — only when the texture or the clip changes —
/// so paint order is preserved without a depth buffer and without per-element pipeline binds.
/// </summary>
internal static class PaintBatcher
{
    internal static void Build(IReadOnlyList<PaintInstruction> instructions, List<PaintBatch> batches)
    {
        batches.Clear();

        if (instructions.Count == 0)
        {
            return;
        }

        PaintInstruction first = instructions[0];
        int start = 0;
        Texture? texture = first.Texture;
        Rectangle clip = first.Clip;

        for (int i = 1; i < instructions.Count; i++)
        {
            PaintInstruction instruction = instructions[i];

            if (ReferenceEquals(instruction.Texture, texture) && instruction.Clip == clip)
            {
                continue;
            }

            batches.Add(new PaintBatch(start, i - start, texture, clip));
            start = i;
            texture = instruction.Texture;
            clip = instruction.Clip;
        }

        batches.Add(new PaintBatch(start, instructions.Count - start, texture, clip));
    }
}
