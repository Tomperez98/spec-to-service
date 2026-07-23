using Microsoft.Accordant;

namespace Model;

public static class BankSpec
{
    public static List<IStepFunction> CreateStepFunctions()
    {
        var deposit = new ContractStepFunction(
            new DepositRequest("account-1", 100m),
            new DepositResponse.Ok(100m),
            (request, state, response) =>
            {
                var r = (DepositRequest)request;
                var s = (BankState)state;
                var balance = s.Accounts.GetValueOrDefault(r.AccountId, 0m);
                var expected = balance + r.Amount;

                if (response is DepositResponse.Ok ok && ok.NewBalance == expected)
                {
                    var next = (BankState)s.Clone();
                    next.Accounts[r.AccountId] = expected;
                    return (true, new StateProfile(next));
                }

                return (false, new StateProfile(s));
            }
        );

        var withdraw = new ContractStepFunction(
            new WithdrawRequest("account-1", 50m),
            new WithdrawResponse.Ok(50m),
            (request, state, response) =>
            {
                var r = (WithdrawRequest)request;
                var s = (BankState)state;

                if (!s.Accounts.TryGetValue(r.AccountId, out var balance))
                    return (response is WithdrawResponse.NotFound, new StateProfile(s));

                if (balance < r.Amount)
                    return (response is WithdrawResponse.InsufficientFunds, new StateProfile(s));

                if (response is WithdrawResponse.Ok ok && ok.NewBalance == balance - r.Amount)
                {
                    var next = (BankState)s.Clone();
                    next.Accounts[r.AccountId] = ok.NewBalance;
                    return (true, new StateProfile(next));
                }

                return (false, new StateProfile(s));
            }
        );

        return new List<IStepFunction> { deposit, withdraw };
    }
}
