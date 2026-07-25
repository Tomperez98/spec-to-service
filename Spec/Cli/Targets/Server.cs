using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Accordant;
using Model;

namespace Cli.Targets;

/// <summary>
/// HTTP target that executes bank operations against a real service and resets
/// state between test cases by draining and closing all accounts.
/// </summary>
public class ServerTarget : ITestingTarget
{
    private readonly BankApiClient _client;

    public string Name { get; }

    public ServerTarget(string url)
    {
        Name = url;
        _client = new BankApiClient(new HttpClient { BaseAddress = new Uri(url) });
    }

    public void Bind(Spec<BankState> spec, TestingContext context)
    {
        BankApiClient.Bind(spec, _client);
        context.Register(_client);
    }

    public Task ResetAsync() => _client.ResetAsync();
}

/// <summary>
/// PostgREST banking API client. Maps raw HTTP responses into the spec's
/// discriminated-union response types.
/// </summary>
public class BankApiClient
{
    private readonly HttpClient _http;

    public BankApiClient(HttpClient http) => _http = http;

    /// <summary>
    /// Wire every spec operation to this client.
    /// </summary>
    public static void Bind(Spec<BankState> spec, BankApiClient client)
    {
        spec.ExecuteWith<BankApiClient>()
            .BindAsync<CreateAccountRequest, CreateAccountResponse>(
                "CreateAccount",
                (c, req) => c.CreateAccountAsync(req))
            .BindAsync<DepositRequest, DepositResponse>(
                "Deposit",
                (c, req) => c.DepositAsync(req))
            .BindAsync<WithdrawRequest, WithdrawResponse>(
                "Withdraw",
                (c, req) => c.WithdrawAsync(req))
            .BindAsync<TransferRequest, TransferResponse>(
                "Transfer",
                (c, req) => c.TransferAsync(req))
            .BindAsync<CloseAccountRequest, CloseAccountResponse>(
                "CloseAccount",
                (c, req) => c.CloseAccountAsync(req));
    }

    public async Task<CreateAccountResponse> CreateAccountAsync(CreateAccountRequest _)
    {
        var response = await _http.PostAsJsonAsync("/rpc/create_account", new { });
        if (!response.IsSuccessStatusCode)
        {
            var err = await ParseError(response);
            throw new InvalidOperationException(
                $"CreateAccount failed: {err.Message} (HTTP {(int)response.StatusCode})");
        }

        var id = (await response.Content.ReadFromJsonAsync<string>())!;
        return new CreateAccountResponse.Ok(id);
    }

    public async Task<DepositResponse> DepositAsync(DepositRequest req)
    {
        var response = await _http.PostAsJsonAsync("/rpc/deposit", new
        {
            account_id = req.AccountId,
            amount = req.Amount,
        });

        if (response.IsSuccessStatusCode)
            return new DepositResponse.Ok(await ReadDecimal(response));

        var err = await ParseError(response);
        return err.Code switch
        {
            "PT404" => new DepositResponse.NotFound(),
            _ => throw new InvalidOperationException(
                $"Deposit: unexpected error {err.Code}: {err.Message}"),
        };
    }

    public async Task<WithdrawResponse> WithdrawAsync(WithdrawRequest req)
    {
        var response = await _http.PostAsJsonAsync("/rpc/withdraw", new
        {
            account_id = req.AccountId,
            amount = req.Amount,
        });

        if (response.IsSuccessStatusCode)
            return new WithdrawResponse.Ok(await ReadDecimal(response));

        var err = await ParseError(response);
        return err.Code switch
        {
            "PT404" => new WithdrawResponse.NotFound(),
            "PT400" when err.Message.Contains("insufficient funds") => new WithdrawResponse.InsufficientFunds(),
            _ => throw new InvalidOperationException(
                $"Withdraw: unexpected error {err.Code}: {err.Message}"),
        };
    }

    public async Task<TransferResponse> TransferAsync(TransferRequest req)
    {
        var response = await _http.PostAsJsonAsync("/rpc/transfer", new
        {
            from_account_id = req.FromAccountId,
            to_account_id = req.ToAccountId,
            amount = req.Amount,
        });

        if (response.IsSuccessStatusCode)
            return new TransferResponse.Ok(await ReadDecimal(response));

        var err = await ParseError(response);
        return err.Code switch
        {
            "PT404" when err.Message.Contains("source") => new TransferResponse.SourceNotFound(),
            "PT404" when err.Message.Contains("target") => new TransferResponse.TargetNotFound(),
            "PT400" when err.Message.Contains("same account") => new TransferResponse.SameAccount(),
            "PT400" when err.Message.Contains("insufficient funds") => new TransferResponse.InsufficientFunds(),
            _ => throw new InvalidOperationException(
                $"Transfer: unexpected error {err.Code}: {err.Message}"),
        };
    }

    public async Task<CloseAccountResponse> CloseAccountAsync(CloseAccountRequest req)
    {
        var response = await _http.PostAsJsonAsync("/rpc/close_account", new
        {
            account_id = req.AccountId,
        });

        if (response.IsSuccessStatusCode)
            return new CloseAccountResponse.Ok();

        var err = await ParseError(response);
        return err.Code switch
        {
            "PT404" => new CloseAccountResponse.NotFound(),
            "PT409" => ParseNonZeroBalance(err.Message),
            _ => throw new InvalidOperationException(
                $"CloseAccount: unexpected error {err.Code}: {err.Message}"),
        };
    }

    /// <summary>
    /// Reset server state by draining and closing all accounts.
    /// </summary>
    public async Task ResetAsync()
    {
        // ponytail: drain-close loop, replace with a truncate RPC if perf matters.
        var listResp = await _http.GetAsync("/account_balances");
        if (!listResp.IsSuccessStatusCode)
            return;

        var accounts = await listResp.Content.ReadFromJsonAsync<List<AccountRow>>();
        if (accounts == null)
            return;

        foreach (var acct in accounts)
        {
            try
            {
                if (acct.Balance > 0)
                {
                    await _http.PostAsJsonAsync("/rpc/withdraw", new
                    {
                        account_id = acct.Id,
                        amount = acct.Balance,
                    });
                }
                await _http.PostAsJsonAsync("/rpc/close_account", new
                {
                    account_id = acct.Id,
                });
            }
            catch
            {
                // Best effort — account may already be gone.
            }
        }
    }

    private static CloseAccountResponse.NonZeroBalance ParseNonZeroBalance(string message)
    {
        foreach (var token in message.Split(' '))
            if (decimal.TryParse(token, out var balance))
                return new CloseAccountResponse.NonZeroBalance(balance);
        return new CloseAccountResponse.NonZeroBalance(0m);
    }

    private static async Task<decimal> ReadDecimal(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        return decimal.Parse(raw);
    }

    private static readonly JsonSerializerOptions _errorJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static async Task<PostgrestError> ParseError(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<PostgrestError>(body, _errorJsonOptions)
               ?? new PostgrestError { Code = "UNKNOWN", Message = body };
    }

    private class PostgrestError
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
    }

    private record AccountRow(string Id, decimal Balance);
}
