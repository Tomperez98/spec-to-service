using Microsoft.Accordant;

namespace Model;

public static class BankSpec
{
    public static Spec<BankState> Create()
    {
        var spec = new Spec<BankState>().WithJsonPrinters();

        spec.Operation<DepositRequest, DepositResponse>(
            "Deposit",
            (req, state) =>
            {
                if (!state.Accounts.TryGetValue(req.AccountId, out var balance))
                    return Expect
                        .That<DepositResponse>(
                            r => r is DepositResponse.Ok { NewBalance: var b } && b == req.Amount,
                            "Missing account: should auto-create with deposited amount"
                        )
                        .ThenState<BankState>(s => s.Accounts[req.AccountId] = req.Amount);

                var newBalance = balance + req.Amount;
                return Expect
                    .That<DepositResponse>(
                        r => r is DepositResponse.Ok { NewBalance: var b } && b == newBalance,
                        $"Should return Ok with balance {newBalance}"
                    )
                    .ThenState<BankState>(s => s.Accounts[req.AccountId] = newBalance);
            }
        );

        spec.Operation<WithdrawRequest, WithdrawResponse>(
            "Withdraw",
            (req, state) =>
            {
                if (!state.Accounts.TryGetValue(req.AccountId, out var balance))
                    return Expect
                        .That<WithdrawResponse>(
                            r => r is WithdrawResponse.NotFound,
                            "Account not found"
                        )
                        .SameState();

                if (balance < req.Amount)
                    return Expect
                        .That<WithdrawResponse>(
                            r => r is WithdrawResponse.InsufficientFunds,
                            "Insufficient funds"
                        )
                        .SameState();

                var newBalance = balance - req.Amount;
                return Expect
                    .That<WithdrawResponse>(
                        r => r is WithdrawResponse.Ok { NewBalance: var b } && b == newBalance,
                        $"Should return Ok with balance {newBalance}"
                    )
                    .ThenState<BankState>(s => s.Accounts[req.AccountId] = newBalance);
            }
        );

        spec.Operation<CloseAccountRequest, CloseAccountResponse>(
            "CloseAccount",
            (req, state) =>
            {
                if (!state.Accounts.TryGetValue(req.AccountId, out var balance))
                    return Expect
                        .That<CloseAccountResponse>(r => r is CloseAccountResponse.NotFound)
                        .SameState();

                if (balance != 0)
                    return Expect
                        .That<CloseAccountResponse>(r =>
                            r is CloseAccountResponse.NonZeroBalance { Balance: var b }
                            && b == balance
                        )
                        .SameState();

                return Expect
                    .That<CloseAccountResponse>(r => r is CloseAccountResponse.Ok)
                    .ThenState<BankState>(s => s.Accounts.Remove(req.AccountId));
            }
        );

        spec.Operation<GetBalanceRequest, GetBalanceResponse>(
            "GetBalance",
            (req, state) =>
            {
                if (!state.Accounts.TryGetValue(req.AccountId, out var balance))
                    return Expect
                        .That<GetBalanceResponse>(r => r is GetBalanceResponse.NotFound)
                        .SameState();

                return Expect
                    .That<GetBalanceResponse>(r =>
                        r is GetBalanceResponse.Ok { Balance: var b } && b == balance
                    )
                    .SameState();
            }
        );

        spec.Operation<TransferRequest, TransferResponse>(
            "Transfer",
            (req, state) =>
            {
                if (!state.Accounts.TryGetValue(req.FromAccountId, out var fromBalance))
                    return Expect
                        .That<TransferResponse>(
                            r => r is TransferResponse.SourceNotFound,
                            "Source account not found"
                        )
                        .SameState();

                if (!state.Accounts.ContainsKey(req.ToAccountId))
                    return Expect
                        .That<TransferResponse>(
                            r => r is TransferResponse.TargetNotFound,
                            "Target account not found"
                        )
                        .SameState();

                if (fromBalance < req.Amount)
                    return Expect
                        .That<TransferResponse>(
                            r => r is TransferResponse.InsufficientFunds,
                            "Insufficient funds"
                        )
                        .SameState();

                var newFromBalance = fromBalance - req.Amount;
                var newToBalance = state.Accounts[req.ToAccountId] + req.Amount;
                return Expect
                    .That<TransferResponse>(
                        r =>
                            r is TransferResponse.Ok { FromNewBalance: var b }
                            && b == newFromBalance,
                        $"Should return Ok with source balance {newFromBalance}"
                    )
                    .ThenState<BankState>(s =>
                    {
                        s.Accounts[req.FromAccountId] = newFromBalance;
                        s.Accounts[req.ToAccountId] = newToBalance;
                    });
            }
        );

        return spec;
    }
}
