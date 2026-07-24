using System.CommandLine;
using Microsoft.Accordant;
using Model;

var smokeNumberArg = new Argument<int>("number", "Smoke test number to run");
var visualizeOption = new Option<bool>(
    "--visualize",
    () => false,
    "Write the DOT graph of the state space to a temp file"
);

var smokeCommand = new Command("smoke", "Run a smoke test by number")
{
    smokeNumberArg,
    visualizeOption,
};
smokeCommand.SetHandler(
    (number, visualize) =>
    {
        var exitCode = number switch
        {
            1 => RunSmoke1(visualize),
            _ => ReportUnknown(number),
        };
        Environment.ExitCode = exitCode;
    },
    smokeNumberArg,
    visualizeOption
);

var root = new RootCommand("Accordant smoke test runner") { smokeCommand };

return await root.InvokeAsync(args);

static int ReportUnknown(int num)
{
    Console.WriteLine($"Unknown smoke test: {num}");
    return 1;
}

static int RunSmoke1(bool visualize)
{
    var label = "Smoke 1: GenerateTests with derivations explores non-trivial graph";
    Console.WriteLine(label);

    var spec = BankSpec.Create();
    var initialState = new BankState();
    var inputs = new InputSet
    {
        spec.GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount")
            .With(new CreateAccountRequest(), "Create source"),
        spec.GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount")
            .With(new CreateAccountRequest(), "Create target"),
    };
    var options = new TestGenerationOptions
    {
        MaxDepth = 4,
        DerivationSelectors = new List<DerivationSelector>
        {
            DerivationSelector.For("Deposit").From("CreateAccount"),
            DerivationSelector.For("Withdraw").From("CreateAccount"),
            DerivationSelector.For("GetBalance").From("CreateAccount"),
            DerivationSelector.For("CloseAccount").From("CreateAccount"),
            DerivationSelector.For("Transfer").From("CreateAccount"),
        },
        RequestTemplates = new Dictionary<string, Func<object>>
        {
            ["Deposit"] = () => new DepositRequest("", 100m),
            ["Withdraw"] = () => new WithdrawRequest("", 50m),
            ["Transfer"] = () => new TransferRequest("", "", 30m),
        },
    };

    try
    {
        var testCases = spec.GenerateTests(initialState, inputs, options);

        if (testCases.Count == 0)
            throw new Exception("Expected non-empty test cases");

        if (!testCases.Any(tc => tc.OperationCalls.Count > 1))
            throw new Exception("Expected at least one test case with >1 operation call");

        var allOpNames = testCases
            .SelectMany(tc => tc.OperationCalls)
            .Select(oc => oc.OperationInput.Operation.Name)
            .ToHashSet();

        foreach (
            var name in new[] { "Deposit", "Withdraw", "GetBalance", "CloseAccount", "Transfer" }
        )
            if (!allOpNames.Contains(name))
                throw new Exception($"Expected operation '{name}' not found in generated tests");

        Console.WriteLine(
            $"  PASS — {testCases.Count} test cases generated covering all 5 operations"
        );

        if (visualize)
        {
            var dot = spec.VisualizeStateSpace(
                initialState,
                inputs,
                options,
                new VisualizationOptions()
            );

            var dotPath = Path.Combine(
                Path.GetTempPath(),
                $"smoke1-{DateTime.Now:yyyyMMdd-HHmmss}.dot"
            );
            File.WriteAllText(dotPath, dot);
            Console.WriteLine($"  DOT graph written to {dotPath}");
        }

        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL — {ex.Message}");
        return 1;
    }
}
