namespace Tests;

using Microsoft.Accordant;
using Model;
using Xunit;

/// <summary>
/// Smoke tests that exercise <c>GenerateTests</c> without a real server.
/// Invariants live in <see cref="BankSpec"/> — the spec self-validates via
/// <c>Invariant.Assert</c> inside each <c>ThenState</c> callback during
/// generation.  An invariant violation makes <c>GenerateTests</c> throw.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void GenerateTests_with_derivations_explores_non_trivial_graph()
    {
        var spec = BankSpec.Create();

        var inputs = new InputSet
        {
            spec.GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateAccount")
                .With(new CreateAccountRequest(), "Create source"),
            spec.GetOperation<CreateAccountRequest, CreateAccountResponse>("CreateTargetAccount")
                .With(new CreateAccountRequest(), "Create target"),
        };
        var options = new TestGenerationOptions
        {
            MaxDepth = 6,
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

        var testCases = spec.GenerateTests(new BankState(), inputs, options);

        Assert.NotEmpty(testCases);
        Assert.Contains(testCases, tc => tc.OperationCalls.Count > 1);

        // Every derived operation must appear — proves derivations are wired correctly.
        var allOpNames = testCases
            .SelectMany(tc => tc.OperationCalls)
            .Select(oc => oc.OperationInput.Operation.Name)
            .ToHashSet();

        foreach (var name in new[] { "Deposit", "Withdraw", "GetBalance", "CloseAccount", "Transfer" })
            Assert.Contains(name, allOpNames);
    }
}
