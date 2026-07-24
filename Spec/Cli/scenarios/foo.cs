using Microsoft.Accordant;
using Model;

namespace Cli.Scenarios;

public class Foo : ITestScenario
{
    private readonly Spec<BankState> _spec;
    private readonly InputSet _inputs;
    private readonly TestGenerationOptions _options;

    public Foo()
    {
        _spec = BankSpec.Create();
        _inputs = new InputSet
        {
            _spec
                .GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount")
                .With(new CreateAccountRequest(), "Create source"),
            _spec
                .GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount")
                .With(new CreateAccountRequest(), "Create target"),
        };
        _options = new TestGenerationOptions
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
    }

    public IList<SequentialTestCase> GenerateTests() =>
        _spec.GenerateTests(new BankState(), _inputs, _options);

    public string VisualizeStateSpace() =>
        _spec.VisualizeStateSpace(new BankState(), _inputs, _options, new VisualizationOptions());
}
