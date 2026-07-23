using Microsoft.Accordant;

namespace Model;

public static class BankSpec
{
    public static Spec<BankState> Create()
    {
        var spec = new Spec<BankState>().WithJsonPrinters();

        spec.Operation<CreateAccountRequest, CreateAccountResponse>(
            "CreateAccount",
            (req, state) =>
            {
                var accountId = Guid.NewGuid().ToString();
                return Expect
                    .That<CreateAccountResponse>(
                        r => r is CreateAccountResponse.Ok { AccountId: var id } && id == accountId,
                        "Should return the new account ID"
                    )
                    .ThenState<BankState>(s => s.Accounts.Add(new Account { Id = accountId, Balance = 0 }));
            }
        );

        spec.Operation<DepositRequest, DepositResponse>(
            "Deposit",
            (req, state) =>
            {
                var idx = state.Accounts.FindIndex(a => a.Id == req.AccountId);
                if (idx == -1)
                    return Expect
                        .That<DepositResponse>(
                            r => r is DepositResponse.NotFound,
                            "Account not found"
                        )
                        .SameState();

                var newBalance = state.Accounts[idx].Balance + req.Amount;
                return Expect
                    .That<DepositResponse>(
                        r => r is DepositResponse.Ok { NewBalance: var b } && b == newBalance,
                        $"Should return Ok with balance {newBalance}"
                    )
                    .ThenState<BankState>(s => s.Accounts[idx] = new Account { Id = req.AccountId, Balance = newBalance });
            }
        );

        spec.Operation<WithdrawRequest, WithdrawResponse>(
            "Withdraw",
            (req, state) =>
            {
                var idx = state.Accounts.FindIndex(a => a.Id == req.AccountId);
                if (idx == -1)
                    return Expect
                        .That<WithdrawResponse>(
                            r => r is WithdrawResponse.NotFound,
                            "Account not found"
                        )
                        .SameState();

                var account = state.Accounts[idx];
                if (account.Balance < req.Amount)
                    return Expect
                        .That<WithdrawResponse>(
                            r => r is WithdrawResponse.InsufficientFunds,
                            "Insufficient funds"
                        )
                        .SameState();

                var newBalance = account.Balance - req.Amount;
                return Expect
                    .That<WithdrawResponse>(
                        r => r is WithdrawResponse.Ok { NewBalance: var b } && b == newBalance,
                        $"Should return Ok with balance {newBalance}"
                    )
                    .ThenState<BankState>(s => s.Accounts[idx] = new Account { Id = req.AccountId, Balance = newBalance });
            }
        );

        spec.Operation<CloseAccountRequest, CloseAccountResponse>(
            "CloseAccount",
            (req, state) =>
            {
                var idx = state.Accounts.FindIndex(a => a.Id == req.AccountId);
                if (idx == -1)
                    return Expect
                        .That<CloseAccountResponse>(r => r is CloseAccountResponse.NotFound)
                        .SameState();

                var account = state.Accounts[idx];
                if (account.Balance != 0)
                    return Expect
                        .That<CloseAccountResponse>(r =>
                            r is CloseAccountResponse.NonZeroBalance { Balance: var b }
                            && b == account.Balance
                        )
                        .SameState();

                return Expect
                    .That<CloseAccountResponse>(r => r is CloseAccountResponse.Ok)
                    .ThenState<BankState>(s => s.Accounts.RemoveAt(idx));
            }
        );

        spec.Operation<GetBalanceRequest, GetBalanceResponse>(
            "GetBalance",
            (req, state) =>
            {
                var account = state.Accounts.Find(a => a.Id == req.AccountId);
                if (account == null)
                    return Expect
                        .That<GetBalanceResponse>(r => r is GetBalanceResponse.NotFound)
                        .SameState();

                return Expect
                    .That<GetBalanceResponse>(r =>
                        r is GetBalanceResponse.Ok { Balance: var b } && b == account.Balance
                    )
                    .SameState();
            }
        );

        spec.Operation<TransferRequest, TransferResponse>(
            "Transfer",
            (req, state) =>
            {
                var fromIdx = state.Accounts.FindIndex(a => a.Id == req.FromAccountId);
                if (fromIdx == -1)
                    return Expect
                        .That<TransferResponse>(
                            r => r is TransferResponse.SourceNotFound,
                            "Source account not found"
                        )
                        .SameState();

                var toIdx = state.Accounts.FindIndex(a => a.Id == req.ToAccountId);
                if (toIdx == -1)
                    return Expect
                        .That<TransferResponse>(
                            r => r is TransferResponse.TargetNotFound,
                            "Target account not found"
                        )
                        .SameState();

                if (state.Accounts[fromIdx].Balance < req.Amount)
                    return Expect
                        .That<TransferResponse>(
                            r => r is TransferResponse.InsufficientFunds,
                            "Insufficient funds"
                        )
                        .SameState();

                var newFromBalance = state.Accounts[fromIdx].Balance - req.Amount;
                var newToBalance = state.Accounts[toIdx].Balance + req.Amount;
                return Expect
                    .That<TransferResponse>(
                        r =>
                            r is TransferResponse.Ok { FromNewBalance: var b }
                            && b == newFromBalance,
                        $"Should return Ok with source balance {newFromBalance}"
                    )
                    .ThenState<BankState>(s =>
                    {
                        s.Accounts[fromIdx] = new Account { Id = req.FromAccountId, Balance = newFromBalance };
                        s.Accounts[toIdx] = new Account { Id = req.ToAccountId, Balance = newToBalance };
                    });
            }
        );

        return spec;
    }
}
