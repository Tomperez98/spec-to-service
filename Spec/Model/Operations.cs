namespace Model;

public record CreateAccountRequest();

public abstract record CreateAccountResponse
{
    private CreateAccountResponse() { }

    public sealed record Ok(string AccountId) : CreateAccountResponse;
}

public record DepositRequest(string AccountId, decimal Amount);

public abstract record DepositResponse
{
    private DepositResponse() { }

    public sealed record Ok(decimal NewBalance) : DepositResponse;

    public sealed record NotFound : DepositResponse;
}

public record WithdrawRequest(string AccountId, decimal Amount);

public abstract record WithdrawResponse
{
    private WithdrawResponse() { }

    public sealed record Ok(decimal NewBalance) : WithdrawResponse;

    public sealed record NotFound : WithdrawResponse;

    public sealed record InsufficientFunds : WithdrawResponse;
}

public record CloseAccountRequest(string AccountId);

public abstract record CloseAccountResponse
{
    private CloseAccountResponse() { }

    public sealed record Ok : CloseAccountResponse;

    public sealed record NotFound : CloseAccountResponse;

    public sealed record NonZeroBalance(decimal Balance) : CloseAccountResponse;
}

public record GetBalanceRequest(string AccountId);

public abstract record GetBalanceResponse
{
    private GetBalanceResponse() { }

    public sealed record Ok(decimal Balance) : GetBalanceResponse;

    public sealed record NotFound : GetBalanceResponse;
}

public record TransferRequest(string FromAccountId, string ToAccountId, decimal Amount);

public abstract record TransferResponse
{
    private TransferResponse() { }

    public sealed record Ok(decimal FromNewBalance) : TransferResponse;

    public sealed record SourceNotFound : TransferResponse;

    public sealed record TargetNotFound : TransferResponse;

    public sealed record InsufficientFunds : TransferResponse;

    public sealed record SameAccount : TransferResponse;
}
