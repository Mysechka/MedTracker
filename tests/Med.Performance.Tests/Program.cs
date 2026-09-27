using BenchmarkDotNet.Running;

namespace Med.Performance.Tests;

internal static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("MedTracker Performance Benchmarks");
            Console.WriteLine("Running all benchmark suites...");
            BenchmarkRunner.Run<ScheduleCalculationBenchmarks>();
            BenchmarkRunner.Run<EntityMappingBenchmarks>();
            BenchmarkRunner.Run<DtoSerializationBenchmarks>();
            BenchmarkRunner.Run<ViewModelStartupBenchmarks>();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
