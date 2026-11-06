using BenchmarkDotNet.Running;

namespace Pixely.Ecs.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args is ["growth", ..])
        {
            GrowthSpikes.Run(args.Length > 1 ? int.Parse(args[1]) : 2_000);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
