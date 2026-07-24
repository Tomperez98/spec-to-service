using System.CommandLine;
using System.Reflection;
using Cli.Smokes;
using Microsoft.Accordant;

var smokeNameArg = new Argument<string>("name", "Smoke test name (class name, case-insensitive)");
var visualizeOption = new Option<bool>(
    "--visualize",
    () => false,
    "Write the DOT graph of the state space to a temp file"
);

var smokeCommand = new Command("smoke", "Run a smoke test by name")
{
    smokeNameArg,
    visualizeOption,
};
smokeCommand.SetHandler(
    (name, visualize) =>
    {
        Environment.ExitCode = RunSmoke(name, visualize);
    },
    smokeNameArg,
    visualizeOption
);

var listCommand = new Command("list", "List available smoke tests");
listCommand.SetHandler(() =>
{
    foreach (var t in DiscoverSmokes())
        Console.WriteLine($"  {t.Name}");
});
smokeCommand.Add(listCommand);

var root = new RootCommand("Accordant smoke test runner") { smokeCommand };

return await root.InvokeAsync(args);

static int RunSmoke(string name, bool visualize)
{
    var type =
        Type.GetType($"Cli.Smokes.{name}", throwOnError: false)
        ?? DiscoverSmokes()
            .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    if (type is null)
        return ReportUnknown(name);

    try
    {
        var testCases = InvokeRun(type);

        if (testCases.Count == 0)
            throw new Exception("Expected non-empty test cases");

        if (!testCases.Any(tc => tc.OperationCalls.Count > 1))
            throw new Exception("Expected at least one test case with >1 operation call");

        Console.WriteLine($"  PASS — {type.Name}: {testCases.Count} test cases");

        if (visualize)
            Visualize(testCases, name);

        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL — {type.Name}: {ex.Message}");
        return 1;
    }
}

static Type[] DiscoverSmokes() =>
    Assembly
        .GetExecutingAssembly()
        .GetTypes()
        .Where(t =>
            t.Namespace == "Cli.Smokes"
            && !t.IsInterface
            && !t.IsAbstract
            && t.IsAssignableTo(typeof(ISmokeTest))
        )
        .ToArray();

static IList<SequentialTestCase> InvokeRun(Type type) =>
    (IList<SequentialTestCase>)type.GetMethod("Run")!.Invoke(null, null)!;

static void Visualize(IList<SequentialTestCase> testCases, string name)
{
    var dotPath = Path.Combine(
        Path.GetTempPath(),
        $"smoke{name}-{DateTime.Now:yyyyMMdd-HHmmss}.dot"
    );
    File.WriteAllText(dotPath, testCases.ToString() ?? "");
    Console.WriteLine($"  DOT graph written to {dotPath}");
}

static int ReportUnknown(string name)
{
    Console.WriteLine($"Unknown smoke test: {name}");
    return 1;
}
