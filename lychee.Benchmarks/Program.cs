using BenchmarkDotNet.Running;

namespace lychee.Benchmarks;

enum E
{
    A, B
}

/// <summary>
/// Entry point that lets BenchmarkDotNet discover and run the benchmark suite.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the benchmarks selected by the command line arguments.
    /// </summary>
    /// <param name="args">BenchmarkDotNet arguments, for example <c>--filter *Add*</c> or <c>--list flat</c>.</param>
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
