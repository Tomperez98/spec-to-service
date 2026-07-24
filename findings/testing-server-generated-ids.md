# Testing an Accordant model with server‑generated IDs (no server yet)

## Context

Bank system, spec‑first. Operations: `CreateAccount`, `Deposit`, `Withdraw`,
`CloseAccount`, `GetBalance`, `Transfer`. Responses are discriminated unions,
state is `BankState { List<Account> }`. No real service exists yet; we want to
test the model in isolation.

The sticking point: `CreateAccount` returns a server‑generated `AccountId`.
How do we test operations that depend on that ID (`Deposit`, `GetBalance`, ...)
when nothing is actually generating it?

## The bug in the original spec

```csharp
spec.Operation<CreateAccountRequest, CreateAccountResponse>("CreateAccount",
    (req, state) =>
    {
        var accountId = Guid.NewGuid().ToString();          // invented here
        return Expect.That<CreateAccountResponse>(
            r => r is CreateAccountResponse.Ok { AccountId: var id } && id == accountId, ...)
            .ThenState<BankState>(s => s.Accounts.Add(new Account { Id = accountId, ... }));
    });
```

The predicate demands the response's ID equal an ID the model just invented in
its own closure. Nothing outside that closure — not `spec.Allows`, not a future
HTTP server — can ever produce that exact Guid. The `CreateAccount` branch is
therefore untestable and, once a server exists, unconformable.

## The pattern: response‑dependent state

For server‑generated identifiers, the **response is the source of truth**. The
model does two things:

1. **Predicate** validates only the *shape* of the response (`Ok`, non‑empty
   ID, no collision with existing accounts).
2. **State transition** reads the ID *out of the response* via the
   response‑dependent overload of `ThenState`:

```csharp
spec.Operation<CreateAccountRequest, CreateAccountResponse>("CreateAccount",
    (req, state) =>
        Expect.That<CreateAccountResponse>(
                r => r is CreateAccountResponse.Ok { AccountId: var id }
                     && !string.IsNullOrWhiteSpace(id)
                     && !state.Accounts.Any(a => a.Id == id),
                "Should return Ok with a fresh, non-empty AccountId")
              .ThenState<BankState>(
                  (resp, s) => s.Accounts.Add(new Account
                  {
                      Id = ((CreateAccountResponse.Ok)resp).AccountId,
                      Balance = 0,
                  }),
                  mock: () => new CreateAccountResponse.Ok(Guid.NewGuid().ToString())));
```

The `mock` argument is the missing piece. It synthesizes a plausible response
during **state graph exploration** (`GenerateTests`, `Allows`) so the framework
can reason about later operations even though no real server is running. At
execution time against a real server, the actual response overrides the mock.

Reference: `Samples/TodoList-Extended` in accordant — same pattern for
server‑generated `TodoId`.

## Testing the model — three tools, three jobs

| Tool | Job |
|---|---|
| `spec.Allows(op, req, resp, state)` | Unit‑test one `(state, request, response)` triple. The **test** plays the server: pick any ID string, pass it as the response, and the spec's predicate + `ThenState` will accept and record it. |
| `spec.GenerateTests(...)` | Explore the state graph. Works once `ThenState` has a `mock`. For multi‑step generated sequences that thread a server ID from `CreateAccount` into `Deposit` etc., add `Derive.From<...>("CreateAccount")...`. |
| `Derive.From(...)` | Only needed for the built‑in generator/executor. Not needed when you hand‑write sequences with `Allows`, because the ID is already in scope. |

## Threading state across `Allows` calls

`Allows` returns `(IsValid, Message, UpdatedStateProfile)`. To chain
operations, pull the next concrete state out of the profile:

```csharp
var create = spec.Allows(spec.GetOperation("CreateAccount"),
    new CreateAccountRequest(),
    new CreateAccountResponse.Ok("acct-1"),
    state);
Assert.True(create.IsValid, create.Message);
state = (BankState)create.UpdatedStateProfile.SingleState();

var deposit = spec.Allows(spec.GetOperation("Deposit"),
    new DepositRequest("acct-1", 100m),
    new DepositResponse.Ok(100m),
    state);
Assert.True(deposit.IsValid, deposit.Message);
state = (BankState)deposit.UpdatedStateProfile.SingleState();
```

`SingleState()` is valid because none of these operations produce a non‑deterministic
state profile. If step functions are ever introduced, use `IsSingleState()` first.

## Rejection cases still fall out naturally

Because the predicate rejects colliding IDs, a "server returns an ID we've
already seen" response is caught:

```csharp
var state = new BankState { Accounts = [new Account { Id = "acct-1", Balance = 0 }] };
var (valid, _, _) = spec.Allows(spec.GetOperation("CreateAccount"),
    new CreateAccountRequest(),
    new CreateAccountResponse.Ok("acct-1"),  // collision
    state);
Assert.False(valid);
```

## When to add `Derive.From`

Not now. Add it when either:

- You want `spec.GenerateTests` to auto‑build sequences where the ID from
  `CreateAccount` feeds `Deposit` / `GetBalance` / `Transfer`, or
- You wire a real client with `.Bind(...)` and run generated tests end‑to‑end.

Signature sketch for later:

```csharp
spec.ConfigureDerivations("Deposit",
    Derive.From<CreateAccountRequest, CreateAccountResponse, DepositRequest>("CreateAccount")
        .When((_, r) => r is CreateAccountResponse.Ok)
        .As((_, r) => new DepositRequest(((CreateAccountResponse.Ok)r).AccountId, 100m)));
```

## Files touched

- `model/Spec.cs` — `CreateAccount` rewritten to use response‑dependent
  `ThenState` with a `mock`.
- `tests/ModelSmokeTests.cs` — added
  `CreateAccount_then_Deposit_then_GetBalance_end_to_end` and
  `CreateAccount_rejects_colliding_id`.

## Takeaway

The model never invents identity. It validates the shape of what came back and
records what it saw. A `mock` in `ThenState` lets exploration proceed without a
server; `Allows` lets a test act as the server for a single call; `Derive.From`
lets the generator chain calls once you're ready to run sequences.
