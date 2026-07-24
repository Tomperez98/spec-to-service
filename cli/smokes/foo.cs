using Microsoft.Accordant;
using Model;

namespace Cli.Smokes;

public class Foo : ISmokeTest
{
    public static IList<SequentialTestCase> Run()
    {
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

        return spec.GenerateTests(initialState, inputs, options);
    }
}
