using Microsoft.Accordant;
using Model;
using Xunit;

namespace Tests;

public class ModelSmokeTests
{
    [Fact]
    public void State_graph_exploration_does_not_crash()
    {
        var spec = BankSpec.Create();
        var inputs = new InputSet();

        inputs.Add(
            new OperationInput(
                "deposit-alice-100",
                spec.GetOperation("Deposit"),
                new DepositRequest("alice", 100m)
            )
        );
        inputs.Add(
            new OperationInput(
                "deposit-bob-50",
                spec.GetOperation("Deposit"),
                new DepositRequest("bob", 50m)
            )
        );
        inputs.Add(
            new OperationInput(
                "withdraw-alice-30",
                spec.GetOperation("Withdraw"),
                new WithdrawRequest("alice", 30m)
            )
        );
        inputs.Add(
            new OperationInput(
                "get-balance-alice",
                spec.GetOperation("GetBalance"),
                new GetBalanceRequest("alice")
            )
        );
        inputs.Add(
            new OperationInput(
                "transfer-alice-to-bob",
                spec.GetOperation("Transfer"),
                new TransferRequest("alice", "bob", 25m)
            )
        );
        inputs.Add(
            new OperationInput(
                "close-alice",
                spec.GetOperation("CloseAccount"),
                new CloseAccountRequest("alice")
            )
        );

        var testCases = spec.GenerateTests(
            new BankState(),
            inputs,
            new TestGenerationOptions { MaxDepth = 5 }
        );

        Assert.True(
            testCases.Count > 0,
            "Should generate at least one test case; model crashed if this fails"
        );
    }

    [Fact]
    public void Deposit_new_account_returns_ok_with_initial_balance()
    {
        var state = new BankState();
        var spec = BankSpec.Create();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Deposit"),
            new DepositRequest("alice", 100m),
            new DepositResponse.Ok(100m),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void Deposit_existing_account_adds_to_balance()
    {
        var spec = BankSpec.Create();
        var state = new BankState { Accounts = { ["alice"] = 100m } };
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Deposit"),
            new DepositRequest("alice", 50m),
            new DepositResponse.Ok(150m),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void Withdraw_not_found_when_account_missing()
    {
        var state = new BankState();
        var spec = BankSpec.Create();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Withdraw"),
            new WithdrawRequest("alice", 50m),
            new WithdrawResponse.NotFound(),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void Withdraw_insufficient_funds_rejected()
    {
        var spec = BankSpec.Create();
        var state = new BankState { Accounts = { ["alice"] = 30m } };
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Withdraw"),
            new WithdrawRequest("alice", 100m),
            new WithdrawResponse.InsufficientFunds(),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void Withdraw_ok_when_sufficient_funds()
    {
        var state = new BankState { Accounts = { ["alice"] = 100m } };
        var spec = BankSpec.Create();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Withdraw"),
            new WithdrawRequest("alice", 40m),
            new WithdrawResponse.Ok(60m),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void GetBalance_not_found_when_account_missing()
    {
        var spec = BankSpec.Create();
        var state = new BankState();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("GetBalance"),
            new GetBalanceRequest("alice"),
            new GetBalanceResponse.NotFound(),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void GetBalance_ok_when_account_exists()
    {
        var state = new BankState { Accounts = { ["alice"] = 75m } };
        var spec = BankSpec.Create();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("GetBalance"),
            new GetBalanceRequest("alice"),
            new GetBalanceResponse.Ok(75m),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void Transfer_source_not_found()
    {
        var spec = BankSpec.Create();
        var state = new BankState { Accounts = { ["bob"] = 100m } };
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Transfer"),
            new TransferRequest("alice", "bob", 50m),
            new TransferResponse.SourceNotFound(),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void Transfer_target_not_found()
    {
        var state = new BankState { Accounts = { ["alice"] = 100m } };
        var spec = BankSpec.Create();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Transfer"),
            new TransferRequest("alice", "bob", 50m),
            new TransferResponse.TargetNotFound(),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void Transfer_insufficient_funds()
    {
        var spec = BankSpec.Create();
        var state = new BankState { Accounts = { ["alice"] = 30m, ["bob"] = 50m } };
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Transfer"),
            new TransferRequest("alice", "bob", 100m),
            new TransferResponse.InsufficientFunds(),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void Transfer_ok_moves_funds()
    {
        var state = new BankState { Accounts = { ["alice"] = 100m, ["bob"] = 50m } };
        var spec = BankSpec.Create();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("Transfer"),
            new TransferRequest("alice", "bob", 30m),
            new TransferResponse.Ok(70m),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void CloseAccount_not_found()
    {
        var spec = BankSpec.Create();
        var state = new BankState();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("CloseAccount"),
            new CloseAccountRequest("alice"),
            new CloseAccountResponse.NotFound(),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void CloseAccount_non_zero_balance()
    {
        var state = new BankState { Accounts = { ["alice"] = 50m } };
        var spec = BankSpec.Create();
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("CloseAccount"),
            new CloseAccountRequest("alice"),
            new CloseAccountResponse.NonZeroBalance(50m),
            state
        );
        Assert.True(valid);
    }

    [Fact]
    public void CloseAccount_ok_when_zero_balance()
    {
        var spec = BankSpec.Create();
        var state = new BankState { Accounts = { ["alice"] = 0m } };
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("CloseAccount"),
            new CloseAccountRequest("alice"),
            new CloseAccountResponse.Ok(),
            state
        );
        Assert.True(valid);
    }
}
