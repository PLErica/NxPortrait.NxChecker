using NxCheck.Cli;
using NxCheck.Core.Checks;
using NxCheck.Core.Expected;
using NxCheck.Core.Model;
using NxCheck.Core.Runners;

var options = CliOptions.Parse(args);

if (options.ShowHelp)
{
    Console.WriteLine(CliOptions.HelpText);
    return 0;
}

if (options.Error is not null)
{
    Console.Error.WriteLine($"오류: {options.Error}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(CliOptions.HelpText);
    return 2;
}

var expected = ExpectedSpecLoader.Load(options.ExpectedPath);

var ctx = new CheckContext
{
    Mode = options.Mode,
    Depth = options.Depth,
    Expected = expected,
    Runner = new CommandRunner(),
    SampleWindow = options.SampleWindow,
    EnableActiveProbe = options.EnableActiveProbe,
    IsInteractive = !Console.IsInputRedirected,
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var engine = new CheckEngine(CheckCatalog.Default());
var results = await engine.RunAllAsync(ctx, cts.Token);

if (options.Json)
    JsonReporter.Write(results);
else
    ConsoleReporter.Write(results);

return ExitCodePolicy.Aggregate(results);
