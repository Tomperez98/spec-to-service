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
                Expect
                    .That<CreateAccountResponse>(
                        r =>
                            r is CreateAccountResponse.Ok { AccountId: var id }
                            && !string.IsNullOrWhiteSpace(id)
                            && !state.Accounts.Any(a => a.Id == id),
                        "Should return Ok with a fresh, non-empty AccountId"
                    )
                    .ThenState<BankState>(
                        (resp, s) =>
                        {
                            var id = ((CreateAccountResponse.Ok)resp).AccountId;
                            s.Accounts.Add(new Account { Id = id, Balance = 0 });
                            Invariant.Assert(
                                s.Accounts.Select(a => a.Id).Distinct().Count() == s.Accounts.Count,
                                "duplicate account IDs"
                            );
                        },
                        mock: () => new CreateAccountResponse.Ok(Guid.NewGuid().ToString())
                    )
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
                    .ThenState<BankState>(s =>
                    {
                        s.Accounts[idx] = new Account { Id = req.AccountId, Balance = newBalance };
                        Invariant.Assert(
                            s.Accounts.All(a => a.Balance >= 0),
                            "balance must be non-negative"
                        );
                    });
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
                    .ThenState<BankState>(s =>
                    {
                        s.Accounts[idx] = new Account { Id = req.AccountId, Balance = newBalance };
                        Invariant.Assert(
                            s.Accounts.All(a => a.Balance >= 0),
                            "balance must be non-negative"
                        );
                    });
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
                    .ThenState<BankState>(s =>
                    {
                        s.Accounts.RemoveAt(idx);
                        Invariant.Assert(
                            s.Accounts.All(a => a.Id != req.AccountId),
                            $"CloseAccount: account {req.AccountId} still present after removal"
                        );
                    });
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

                if (fromIdx == toIdx)
                    return Expect
                        .That<TransferResponse>(
                            r => r is TransferResponse.SameAccount,
                            "Cannot transfer to same account"
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
                        var totalBefore = s.Accounts.Sum(a => a.Balance);
                        s.Accounts[fromIdx] = new Account
                        {
                            Id = req.FromAccountId,
                            Balance = newFromBalance,
                        };
                        s.Accounts[toIdx] = new Account
                        {
                            Id = req.ToAccountId,
                            Balance = newToBalance,
                        };
                        Invariant.Assert(
                            s.Accounts.All(a => a.Balance >= 0),
                            "balance must be non-negative"
                        );
                        Invariant.Assert(
                            s.Accounts.Sum(a => a.Balance) == totalBefore,
                            $"Transfer changed total balance: {totalBefore} → {s.Accounts.Sum(a => a.Balance)}"
                        );
                    });
            }
        );

        // Derivations: thread server-generated account IDs from CreateAccount and
        // CreateTargetAccount into downstream ops so the generator can produce
        // non-trivial sequences.
        spec.ConfigureDerivations(
            "Deposit",
            Derive
                .From<CreateAccountRequest, CreateAccountResponse, DepositRequest>("CreateAccount")
                .When((_, resp) => resp is CreateAccountResponse.Ok)
                .As(
                    (_, resp, template) =>
                        new DepositRequest(
                            ((CreateAccountResponse.Ok)resp).AccountId,
                            template.Amount
                        )
                )
        );

        spec.ConfigureDerivations(
            "Withdraw",
            Derive
                .From<CreateAccountRequest, CreateAccountResponse, WithdrawRequest>("CreateAccount")
                .When((_, resp) => resp is CreateAccountResponse.Ok)
                .As(
                    (_, resp, template) =>
                        new WithdrawRequest(
                            ((CreateAccountResponse.Ok)resp).AccountId,
                            template.Amount
                        )
                )
        );

        spec.ConfigureDerivations(
            "GetBalance",
            Derive
                .From<CreateAccountRequest, CreateAccountResponse, GetBalanceRequest>(
                    "CreateAccount"
                )
                .When((_, resp) => resp is CreateAccountResponse.Ok)
                .As((_, resp) => new GetBalanceRequest(((CreateAccountResponse.Ok)resp).AccountId))
        );

        spec.ConfigureDerivations(
            "CloseAccount",
            Derive
                .From<CreateAccountRequest, CreateAccountResponse, CloseAccountRequest>(
                    "CreateAccount"
                )
                .When((_, resp) => resp is CreateAccountResponse.Ok)
                .As(
                    (_, resp) => new CloseAccountRequest(((CreateAccountResponse.Ok)resp).AccountId)
                )
        );

        // Transfer: each derivation fills its own field, preserving the other
        // from the template or prior derivation. The generator composes both.
        spec.ConfigureDerivations(
            "Transfer",
            Derive
                .From<CreateAccountRequest, CreateAccountResponse, TransferRequest>("CreateAccount")
                .When((_, resp) => resp is CreateAccountResponse.Ok)
                .As(
                    (_, resp, template) =>
                        new TransferRequest(
                            ((CreateAccountResponse.Ok)resp).AccountId,
                            template.ToAccountId,
                            template.Amount
                        )
                ),
            Derive
                .From<CreateAccountRequest, CreateAccountResponse, TransferRequest>("CreateAccount")
                .When((_, resp) => resp is CreateAccountResponse.Ok)
                .As(
                    (_, resp, template) =>
                        new TransferRequest(
                            template.FromAccountId,
                            ((CreateAccountResponse.Ok)resp).AccountId,
                            template.Amount
                        )
                )
        );

        return spec;
    }
}
