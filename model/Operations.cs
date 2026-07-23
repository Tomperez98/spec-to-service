namespace Model;

public record DepositRequest(string AccountId, decimal Amount);

public abstract record DepositResponse
{
    private DepositResponse() { }
    public sealed record Ok(decimal NewBalance) : DepositResponse;
}

public record WithdrawRequest(string AccountId, decimal Amount);

public abstract record WithdrawResponse
{
    private WithdrawResponse() { }
    public sealed record Ok(decimal NewBalance) : WithdrawResponse;
    public sealed record NotFound : WithdrawResponse;
    public sealed record InsufficientFunds : WithdrawResponse;
}
