# Spec to Service

**Write the spec. Test the spec. Generate the service. Validate everything.**

A workflow that flips the traditional development cycle: instead of building a service and then writing tests, you write an executable behavioral specification first, validate the model itself, then generate or build the implementation from the spec, and mechanically verify it conforms.

Powered by [Microsoft Accordant](https://github.com/microsoft/accordant) — a model-based testing framework for .NET.

---

## Philosophy

```
Traditional:  Build → Write Tests → Hope Tests Cover Edge Cases
Spec-to-Svc:  Spec → Validate Spec → Build → Conformance Check ✓
```

The spec is the source of truth. It describes what the system should do in one place: every operation, every state transition, every error condition. Everything else derives from it.

---

## The Pipeline

```
┌──────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ 1. SPEC  │ ──▶ │ 2. VALIDATE  │ ──▶ │ 3. BUILD     │ ──▶ │ 4. VERIFY    │
│          │     │    MODEL     │     │   SERVICE    │     │              │
│ Define   │     │              │     │              │     │              │
│ state,   │     │ GenerateTests│     │ AI or manual │     │ Run tests vs │
│ ops,     │     │ RandomWalk   │     │ generates    │     │ real server, │
│ rules    │     │ Trace replay │     │ ASP.NET impl │     │ spec.Allows()│
└──────────┘     └──────────────┘     └──────────────┘     └──────────────┘
```

### Step 1: Write the Spec

Three files define the entire behavioral contract:

| File | Purpose |
|------|---------|
| `Spec/State.cs` | What the system remembers — `[State]` classes |
| `Spec/Operations.cs` | What each operation does — `Expect.That(...)` rules |
| `Spec/Spec.cs` | Wires state + operations together |

```csharp
// Example: BankAccount spec fragment
spec.Operation<WithdrawRequest, BankResponse>("Withdraw", (request, state) =>
{
    if (!state.Accounts.TryGetValue(request.AccountId, out var balance))
        return Expect.That(r => r.IsNotFound).SameState();

    if (balance < request.Amount)
        return Expect.That(r => r.IsBadRequest).SameState();

    return Expect.That(r => r.IsSuccess && r.Balance == balance - request.Amount)
           .ThenState(s => s.Accounts[request.AccountId] = balance - request.Amount);
});
```

### Step 2: Validate the Model (No Server Needed)

Before building anything, prove the spec is correct:

| Technique | What it catches |
|-----------|----------------|
| `GenerateTests()` without a server | Runtime errors in the spec (null refs, missing keys) |
| `CreateRandomWalk(n, seed)` | Fuzz the model — random paths through the state graph |
| Trace database replay | Silent regressions — did a spec change break previously-valid behavior? |
| `spec.Allows(op, req, resp, state)` | Manual probing of edge cases |
| `VisualizeStateSpace()` | See the state machine (DOT → PNG) |

```csharp
// Smoke-test: does the model crash during exploration?
var testCases = spec.GenerateTests(initialState, inputs,
    new TestGenerationOptions { MaxDepth = 5 });
// No exception = model is structurally sound
```

### Step 3: Build the Service

The spec IS the implementation spec. Feed it to an AI agent with:

> "Build an ASP.NET Core API that conforms to this Accordant spec. The spec defines every endpoint's behavior. Implement controllers, a DbContext with EF Core + SQLite, and proper error handling."

Or build it by hand — the spec tells you exactly what each endpoint must do.

### Step 4: Verify Conformance

Bind the spec to the real server and run:

```csharp
spec.ExecuteWith<BankApiClient>()
    .BindAsync<WithdrawRequest, BankResponse>("Withdraw",
        (client, req) => client.WithdrawAsync(req.AccountId, req.Amount));

var results = await spec.RunTests(context, initialState, testCases, options);
// 0 failures = implementation conforms to spec ✓
```

For non-.NET services: export test plans to JSON, execute from any language, capture traces, validate with `spec.Allows()`.

---

## What Accordant Gives You

| Capability | Description |
|------------|-------------|
| State graph exploration | From N inputs, generates all meaningful operation sequences |
| Linearizability checking | Finds race conditions in concurrent operations |
| Async/polling support | Models background jobs and eventual consistency |
| Response-dependent state | Captures server-generated values (timestamps, IDs) |
| Clear failure diagnostics | "Expected X, got Y, state was Z" |
| Language-agnostic validation | Export JSON test plans, validate traces from any system |
| Pluggable algorithms | StateCoverage, TransitionCoverage, RandomWalk, or custom |

---

## Project Structure

```
spec-to-service/
├── README.md
├── spec-to-service.sln
├── src/
│   └── Spec/                  # The behavioral specification
│       ├── Spec.csproj
│       ├── State.cs            # [State] classes
│       ├── Operations.cs       # Operation definitions + Expect rules
│       └── Spec.cs             # Wires it all together
├── tests/
│   ├── Spec.Tests/             # Tests that validate the model itself
│   │   ├── Spec.Tests.csproj
│   │   ├── ModelValidation.cs  # GenerateTests, RandomWalk, trace replay
│   │   └── golden-traces/      # Recorded traces for regression detection
│   └── Conformance.Tests/      # Tests that validate the server
│       ├── Conformance.Tests.csproj
│       ├── ConformanceTests.cs # Bind spec to server, run generated tests
│       └── ApiClient.cs        # HttpClient wrapper for the service
└── server/
    └── Api/                    # The ASP.NET Core service (generated/built)
        └── Api.csproj
```

---

## Getting Started

```bash
# 1. Create the spec project
dotnet new classlib -n Spec -o src/Spec
cd src/Spec
dotnet add package Microsoft.Accordant

# 2. Define your state, operations, and spec (see docs above)

# 3. Validate the model
cd ../../tests/Spec.Tests
dotnet test --filter ModelValidation

# 4. Build the server (AI-assisted or manual)

# 5. Verify conformance
dotnet test --filter ConformanceTests
```

---

## Key Concepts

- **The spec is an oracle** — `spec.Allows(op, request, response, state)` returns whether a response is valid. Everything flows from this.
- **State is simplified** — the spec tracks only what matters for correctness, not the implementation's internal details.
- **Model first** — validate the spec before building. Runtime errors and logical gaps surface early.
- **Fuzzing is free** — `RandomWalk` + trace replay = continuous model fuzzing. Add production traces for even broader coverage.

---

## Resources

- [Accordant Documentation](https://microsoft.github.io/accordant)
- [Accordant GitHub](https://github.com/microsoft/accordant)
- [Accordant Starter Template](https://github.com/microsoft/accordant/tree/main/agent/starter)
- [Tutorials](https://microsoft.github.io/accordant/docs/tutorials/index.html)

## License

MIT
