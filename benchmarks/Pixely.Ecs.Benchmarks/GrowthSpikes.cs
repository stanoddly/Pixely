using System.Diagnostics;
using System.Runtime;
using Arch.Buffer;
using Arch.Core;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Grows one archetype by a batch of entities per frame, as a spawn wave or a level load does, and reports the slowest frames and the collections
// they caused. BenchmarkDotNet reports means, which hide the frame that copies a large column or waits for a collection.
// Run with: dotnet run -c Release --project benchmarks/Pixely.Ecs.Benchmarks/Pixely.Ecs.Benchmarks.csproj -- growth [entities per frame]
public static class GrowthSpikes
{
    private const int Target = 1_000_000;
    private static int PerFrame = 2_000;
    // Every frame spawns PerFrame entities, so the frames round the target up.
    private static int Frames;
    private static int Total;
    private const int Runs = 7;

    // Asks for room in its first update when room is more than zero.
    internal sealed class SpawnSystem(MovingSpawner spawner, int room) : ISystem
    {
        private int _room = room;

        public void Update()
        {
            if (_room > 0)
            {
                spawner.EnsureRoomFor(_room);
                _room = 0;
            }

            for (int i = 0; i < PerFrame; i++)
            {
                spawner.Spawn(new Position(i, i), new Velocity(1, 1));
            }
        }
    }

    private static readonly ComponentType[] s_movingTypes = [Component<Position>.ComponentType, Component<Velocity>.ComponentType];

    private readonly record struct RunResult(double TotalMs, double MaxFrameMs, double P99FrameMs, int Gen0, int Gen1, int Gen2, double PauseMs, long AllocatedMb);

    public static void Run(int perFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perFrame);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(perFrame, Target);
        PerFrame = perFrame;
        Frames = (Target + perFrame - 1) / perFrame;
        Total = Frames * perFrame;
        Console.WriteLine($"{Total:N0} entities, {PerFrame:N0} per frame, median of {Runs} interleaved runs after warmup, {(GCSettings.IsServerGC ? "server" : "workstation")} GC, latency mode {GCSettings.LatencyMode}");
        Console.WriteLine($"| Case | Total ms | Max frame ms | P99 frame ms | Gen0 | Gen1 | Gen2 | GC pause ms | Allocated MB |");
        Console.WriteLine($"|---|---|---|---|---|---|---|---|---|");
        List<(string Name, Func<(Action Frame, Action? Dispose)> CreateFrame)> cases = [];
        Add("Pixely Spawn", () =>
        {
            SystemScheduler scheduler = new SystemScheduler(new World());
            scheduler.Add(builder => new SpawnSystem(builder.Spawner<MovingSpawner>(), 0));
            return (scheduler.Update, null);
        });
        // The request is part of the first frame, so its allocation counts.
        Add("Pixely Spawn, EnsureRoomFor", () =>
        {
            SystemScheduler scheduler = new SystemScheduler(new World());
            scheduler.Add(builder => new SpawnSystem(builder.Spawner<MovingSpawner>(), Total));
            return (scheduler.Update, null);
        });
        // Pixely's spawns wait for the scheduler's sync point, so Arch records its creations in a CommandBuffer and plays them back each frame.
        Add("Arch CommandBuffer", () =>
        {
            ArchWorld world = ArchWorld.Create();
            CommandBuffer buffer = new CommandBuffer();
            return (() =>
            {
                for (int i = 0; i < PerFrame; i++)
                {
                    ArchEntity entity = buffer.Create(s_movingTypes);
                    buffer.Set(in entity, new Position(i, i));
                    buffer.Set(in entity, new Velocity(1, 1));
                }

                buffer.Playback(world);
            }, () =>
            {
                buffer.Dispose();
                ArchWorld.Destroy(world);
            });
        });

        // The first measurements of a process run slower, whatever the case, so every case is warmed up before any is measured. Tiered compilation
        // also recompiles hot methods with full optimization only after a delay without new compilations, hence the pause. The rounds then measure
        // every case once each, so whatever drift remains spreads over all cases.
        foreach ((_, Func<(Action Frame, Action? Dispose)> createFrame) in cases)
        {
            for (int warmup = 0; warmup < 3; warmup++)
            {
                Measure(createFrame);
            }
        }

        Thread.Sleep(1000);
        List<RunResult>[] results = [.. cases.Select(_ => new List<RunResult>())];
        for (int round = 0; round < Runs; round++)
        {
            for (int i = 0; i < cases.Count; i++)
            {
                results[i].Add(Measure(cases[i].CreateFrame));
            }
        }

        for (int i = 0; i < cases.Count; i++)
        {
            Report(cases[i].Name, results[i]);
        }

        void Add(string name, Func<(Action Frame, Action? Dispose)> createFrame) => cases.Add((name, createFrame));
    }

    private static void Report(string name, List<RunResult> results)
    {
        double Median(Func<RunResult, double> select) => results.Select(select).Order().ElementAt(results.Count / 2);
        Console.WriteLine($"| {name} | {Median(r => r.TotalMs):F1} | {Median(r => r.MaxFrameMs):F2} | {Median(r => r.P99FrameMs):F2} | {Median(r => r.Gen0)} | {Median(r => r.Gen1)} | {Median(r => r.Gen2)} | {Median(r => r.PauseMs):F2} | {Median(r => r.AllocatedMb)} |");
    }

    private static RunResult Measure(Func<(Action Frame, Action? Dispose)> createFrame)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        (Action frame, Action? dispose) = createFrame();
        double[] frames = new double[Frames];
        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);
        TimeSpan pause = GC.GetTotalPauseDuration();
        long allocated = GC.GetTotalAllocatedBytes(true);
        Stopwatch total = Stopwatch.StartNew();
        for (int i = 0; i < frames.Length; i++)
        {
            long start = Stopwatch.GetTimestamp();
            frame();
            frames[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        total.Stop();
        double[] sorted = [.. frames.Order()];
        RunResult result = new RunResult(total.Elapsed.TotalMilliseconds, sorted[^1], sorted[(int)(sorted.Length * 0.99)], GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2, (GC.GetTotalPauseDuration() - pause).TotalMilliseconds, (GC.GetTotalAllocatedBytes(true) - allocated) / (1024 * 1024));
        dispose?.Invoke();
        return result;
    }
}
