using System.CommandLine;
using System.Reflection;
using Cli.Scenarios;
using Microsoft.Accordant;

var scenarioNameArg = new Argument<string>("name", "Scenario name (class name, case-insensitive)");
var visualizeOption = new Option<bool>(
    "--visualize",
    () => false,
    "Write the DOT graph of the state space to a temp file"
);

var scenarioCommand = new Command("scenario", "Run a test scenario by name")
{
    scenarioNameArg,
    visualizeOption,
};
scenarioCommand.SetHandler(
    (name, visualize) =>
    {
        Environment.ExitCode = RunScenario(name, visualize);
    },
    scenarioNameArg,
    visualizeOption
);

var listCommand = new Command("list", "List available test scenarios");
listCommand.SetHandler(() =>
{
    foreach (var t in DiscoverScenarios())
        Console.WriteLine($"  {t.Name}");
});
scenarioCommand.Add(listCommand);

var root = new RootCommand("Accordant test scenario runner") { scenarioCommand };

return await root.InvokeAsync(args);

static int RunScenario(string name, bool visualize)
{
    var type = DiscoverScenarios()
        .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    if (type is null)
    {
        Console.WriteLine($"Unknown scenario: {name}");
        return 1;
    }

    var scenario = (ITestScenario)Activator.CreateInstance(type)!;
    var testCases = scenario.GenerateTests();

    if (testCases.Count == 0)
        throw new InvalidOperationException("Expected non-empty test cases");

    if (!testCases.Any(tc => tc.OperationCalls.Count > 1))
        throw new InvalidOperationException("Expected at least one test case with >1 operation call");

    Console.WriteLine($"  PASS — {type.Name}: {testCases.Count} test cases");

    if (visualize)
    {
        var dotPath = Path.Combine(
            Path.GetTempPath(),
            $"scenario-{name}-{DateTime.Now:yyyyMMdd-HHmmss}.dot"
        );
        File.WriteAllText(dotPath, scenario.VisualizeStateSpace());
        Console.WriteLine($"  DOT graph written to {dotPath}");
    }

    return 0;
}

static Type[] DiscoverScenarios() =>
    Assembly
        .GetExecutingAssembly()
        .GetTypes()
        .Where(t =>
            t.Namespace == "Cli.Scenarios"
            && !t.IsInterface
            && !t.IsAbstract
            && t.IsAssignableTo(typeof(ITestScenario))
        )
        .ToArray();
