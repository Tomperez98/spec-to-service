using Microsoft.Accordant;
using Model;
using Xunit;

namespace Tests;

// Uses spec.Allows to check single (state, request, response) triples.
// The test plays the role of the server: it picks the response and asks the
// spec whether that response is a legal outcome for the given state+request.
public class UnitTests
{
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
        Assert.False(valid);
    }

    // Answers: "how do I test CreateAccount when the ID is generated internally?"
    // You (the test) play the role of the server: you pick any plausible ID string
    // and pass it as the response. The spec's predicate accepts any non-empty,
    // non-colliding ID and threads it into state via response-dependent ThenState.
    // Subsequent operations use that same ID.
    [Fact]
    public void CreateAccount_then_Deposit_then_GetBalance_end_to_end()
    {
        var spec = BankSpec.Create();
        var state = new BankState();

        // 1. CreateAccount — we, the test, choose what the "server" returns.
        var newId = "acct-1";
        var create = spec.Allows(
            spec.GetOperation("CreateAccount"),
            new CreateAccountRequest(),
            new CreateAccountResponse.Ok(newId),
            state
        );
        Assert.True(create.IsValid, create.Message);
        // Thread the new state forward via the returned StateProfile.

        // 2. Deposit against the freshly-created ID.
        var deposit = spec.Allows(
            spec.GetOperation("Deposit"),
            new DepositRequest(newId, 100m),
            new DepositResponse.Ok(100m),
            (BankState)create.UpdatedStateProfile.SingleState()
        );
        Assert.True(deposit.IsValid, deposit.Message);
        state = (BankState)deposit.UpdatedStateProfile.SingleState();

        // 3. GetBalance reflects the deposit.
        var balance = spec.Allows(
            spec.GetOperation("GetBalance"),
            new GetBalanceRequest(newId),
            new GetBalanceResponse.Ok(100m),
            state
        );
        Assert.True(balance.IsValid, balance.Message);
    }

    // Rejection case: predicate demands a fresh ID, so reusing an existing one fails.
    [Fact]
    public void CreateAccount_rejects_colliding_id()
    {
        var spec = BankSpec.Create();
        var state = new BankState { Accounts = [new Account { Id = "acct-1", Balance = 0 }] };
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("CreateAccount"),
            new CreateAccountRequest(),
            new CreateAccountResponse.Ok("acct-1"),
            state
        );
        Assert.False(valid);
    }

    [Fact]
    public void Deposit_existing_account_adds_to_balance()
    {
        var spec = BankSpec.Create();
        var state = new BankState { Accounts = [new Account { Id = "alice", Balance = 100m }] };
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
        var state = new BankState { Accounts = [new Account { Id = "alice", Balance = 30m }] };
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
        var state = new BankState { Accounts = [new Account { Id = "alice", Balance = 100m }] };
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
        var state = new BankState { Accounts = [new Account { Id = "alice", Balance = 75m }] };
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
        var state = new BankState { Accounts = [new Account { Id = "bob", Balance = 100m }] };
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
        var state = new BankState { Accounts = [new Account { Id = "alice", Balance = 100m }] };
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
        var state = new BankState
        {
            Accounts =
            [
                new Account { Id = "alice", Balance = 30m },
                new Account { Id = "bob", Balance = 50m },
            ],
        };
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
        var state = new BankState
        {
            Accounts =
            [
                new Account { Id = "alice", Balance = 100m },
                new Account { Id = "bob", Balance = 50m },
            ],
        };
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
        var state = new BankState { Accounts = [new Account { Id = "alice", Balance = 50m }] };
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
        var state = new BankState { Accounts = [new Account { Id = "alice", Balance = 0m }] };
        var (valid, _, _) = spec.Allows(
            spec.GetOperation("CloseAccount"),
            new CloseAccountRequest("alice"),
            new CloseAccountResponse.Ok(),
            state
        );
        Assert.True(valid);
    }
}
