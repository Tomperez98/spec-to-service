using System.CommandLine;
using Cli.Scenarios;
using Cli.Targets;
using Microsoft.Accordant;
using Model;

// ── Registration ─────────────────────────────────────────────────────────

var scenarios = new Dictionary<string, ITestScenario>(StringComparer.OrdinalIgnoreCase)
{
    ["Foo"] = new Foo(),
    ["Bar"] = new Bar(),
};

var targets = new Dictionary<string, ITestingTarget>(StringComparer.OrdinalIgnoreCase)
{
    ["Server"] = new ServerTarget("http://localhost:3000"),
};

// ── CLI ──────────────────────────────────────────────────────────────────

var nameArg = new Argument<string>("name", "Scenario name");
var targetOpt = new Option<string?>("--target", () => null, "Target server, or omit for spec-only validation");
var visualizeOpt = new Option<bool>("--visualize", () => false, "Write DOT graph to temp file");

var scenarioCmd = new Command("scenario", "Run a test scenario") { nameArg, targetOpt, visualizeOpt };
scenarioCmd.SetHandler(
    async (name, target, visualize) =>
    {
        if (!scenarios.TryGetValue(name, out var scenario))
        {
            Console.WriteLine($"Unknown scenario: {name}. Available: {string.Join(", ", scenarios.Keys)}");
            Environment.ExitCode = 1;
            return;
        }

        ITestingTarget? resolved = null;
        if (target is not null && !targets.TryGetValue(target, out resolved))
        {
            Console.WriteLine($"Unknown target: {target}. Available: {string.Join(", ", targets.Keys)}");
            Environment.ExitCode = 1;
            return;
        }

        Environment.ExitCode = resolved is null
            ? RunSpecOnly(scenario, visualize)
            : await RunAgainstTarget(scenario, resolved, visualize);
    },
    nameArg, targetOpt, visualizeOpt
);

var listCmd = new Command("list", "List registered scenarios and targets");
listCmd.SetHandler(() =>
{
    Console.WriteLine("Scenarios:");
    foreach (var name in scenarios.Keys) Console.WriteLine($"  {name}");
    Console.WriteLine("Targets:");
    foreach (var name in targets.Keys) Console.WriteLine($"  {name}");
});
scenarioCmd.Add(listCmd);

var root = new RootCommand("Accordant test scenario runner") { scenarioCmd };
return await root.InvokeAsync(args);

// ── Spec-only ────────────────────────────────────────────────────────────

static int RunSpecOnly(ITestScenario scenario, bool visualize)
{
    var testCases = scenario.GenerateTests();

    if (testCases.Count == 0)
        throw new InvalidOperationException("Expected non-empty test cases");
    if (!testCases.Any(tc => tc.OperationCalls.Count > 1))
        throw new InvalidOperationException("Expected at least one test case with >1 operation call");

    Console.WriteLine($"  PASS — {scenario.GetType().Name}: {testCases.Count} test cases");

    if (visualize)
        WriteDot(scenario);

    return 0;
}

// ── Live execution ───────────────────────────────────────────────────────

static async Task<int> RunAgainstTarget(ITestScenario scenario, ITestingTarget target, bool visualize)
{
    var spec = scenario.GetSpec();
    var testCases = scenario.GenerateTests();
    Console.WriteLine($"  {testCases.Count} test cases");

    var context = spec.CreateTestingContext();
    target.Bind(spec, context);

    Console.WriteLine($"Running tests against {target.Name}...");
    var results = await spec.RunTests(context, new BankState(), testCases,
        new TestExecutionOptions
        {
            StopOnFirstFailure = false,
            BeforeEachAsync = _ => target.ResetAsync(),
        });

    var failures = results.Where(r => !r.Success).ToList();
    Console.WriteLine();
    Console.WriteLine($"Results: {results.Count - failures.Count} passed, {failures.Count} failed (of {results.Count} total)");

    foreach (var f in failures.Take(10))
    {
        Console.WriteLine($"  FAIL: {f.LastFailureMessage}");
        if (f.LogFilePath is not null) Console.WriteLine($"    Log: {f.LogFilePath}");
    }
    if (failures.Count > 10)
        Console.WriteLine($"  ... and {failures.Count - 10} more");

    if (visualize)
        WriteDot(scenario);

    return failures.Count > 0 ? 1 : 0;
}

// ── Visualize ────────────────────────────────────────────────────────────

static void WriteDot(ITestScenario scenario)
{
    var dotPath = Path.Combine(
        Path.GetTempPath(),
        $"scenario-{scenario.GetType().Name}-{DateTime.Now:yyyyMMdd-HHmmss}.dot"
    );
    File.WriteAllText(dotPath, scenario.VisualizeStateSpace());
    Console.WriteLine($"  DOT graph written to {dotPath}");
}
