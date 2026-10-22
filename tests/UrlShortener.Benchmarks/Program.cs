using BenchmarkDotNet.Running;

namespace UrlShortener.Benchmarks
{
    /// <summary>Entry point that runs the benchmarks selected on the command line (TSK-07 adds the first ones).</summary>
    public static class Program
    {
        /// <summary>Runs BenchmarkDotNet's switcher over this assembly.</summary>
        /// <param name="args">BenchmarkDotNet command-line arguments, e.g. <c>--filter *</c>.</param>
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
