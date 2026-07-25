# Spec to Service

Write the spec. Test the spec. Generate the service. Validate everything.

A workflow that flips development: define an executable behavioral specification first, validate it, then build the implementation and mechanically verify conformance.

Powered by [Microsoft Accordant](https://github.com/microsoft/accordant).

## How to Run

```bash
# Start Postgres
docker run --rm -e POSTGRES_PASSWORD=postgres -p 5432:5432 postgres:16

# Run migrations
bun run db:migrate

# Start PostgREST
DATABASE_URL="postgres://postgres:postgres@localhost:5432/postgres" \
  PGRST_DB_ANON_ROLE="postgres" \
  postgrest postgrest.conf

# Run spec tests against the server
dotnet run --project Spec/Cli -- scenario Foo --target server
```

## Project Structure

```
├── Spec/
│   ├── Model/              # The behavioral specification
│   │   ├── Model.csproj
│   │   ├── State.cs        # BankState — what the system remembers
│   │   ├── Operations.cs   # Request/response types for each operation
│   │   └── Spec.cs         # Expect.That rules per operation
│   ├── Cli/                # CLI entry point (references Model)
│   │   ├── Cli.csproj
│   │   ├── Program.cs
│   │   ├── Scenarios/      # Test scenario implementations
│   │   │   ├── ITestScenario.cs
│   │   │   ├── Foo.cs
│   │   │   └── Bar.cs
│   │   └── Targets/        # Target adapters for live testing
│   │       ├── ITestingTarget.cs
│   │       └── Server.cs   # ServerTarget (HTTP)
│   └── Tests/              # Unit tests
│       ├── Tests.csproj
│       └── UnitTests.cs
├── spec-to-service.slnx
├── justfile                # Build/test/lint/format commands
│
├── .agents/skills/operations/SKILL.md   # Accordant operations pattern reference
│
├── AGENTS.md               # Agent instructions
├── CLAUDE.md
└── README.md
```

## Operations

Three operations define the bank account contract:

| Operation | Guards | Mutates |
|-----------|--------|---------|
| `Deposit` | Missing account → auto-creates | `Accounts[id] += amount` |
| `Withdraw` | NotFound, InsufficientFunds | `Accounts[id] -= amount` |
| `Transfer` | SourceNotFound, TargetNotFound, InsufficientFunds | Both accounts |

```csharp
// Example: Withdraw operation
spec.Operation<WithdrawRequest, WithdrawResponse>("Withdraw", (req, state) =>
{
    if (!state.Accounts.TryGetValue(req.AccountId, out var balance))
        return Expect.That<WithdrawResponse>(r => r is WithdrawResponse.NotFound).SameState();

    if (balance < req.Amount)
        return Expect.That<WithdrawResponse>(r => r is WithdrawResponse.InsufficientFunds).SameState();

    var newBalance = balance - req.Amount;
    return Expect.That<WithdrawResponse>(
        r => r is WithdrawResponse.Ok { NewBalance: var b } && b == newBalance)
        .ThenState<BankState>(s => s.Accounts[req.AccountId] = newBalance);
});
```

## CLI Usage

```bash
# Restore & build
dotnet restore
dotnet build

# List registered scenarios and targets
dotnet run --project Spec/Cli scenario list

# Validate a scenario against the spec only (no live server)
dotnet run --project Spec/Cli scenario Foo
dotnet run --project Spec/Cli scenario Bar

# Run a scenario against a live server
dotnet run --project Spec/Cli scenario Foo --target Server

# Generate a DOT state-space graph
dotnet run --project Spec/Cli scenario Foo --visualize
```

### Adding a scenario

Implement `ITestScenario` and register it in `Program.cs`:

```csharp
var scenarios = new Dictionary<string, ITestScenario>(StringComparer.OrdinalIgnoreCase)
{
    ["Foo"] = new Foo(),
    ["Bar"] = new Bar(),
};
```

### Adding a target

Implement `ITestingTarget` (e.g. `ServerTarget`) and register it:

```csharp
var targets = new Dictionary<string, ITestingTarget>(StringComparer.OrdinalIgnoreCase)
{
    ["Server"] = new ServerTarget("http://localhost:3000"),
};
```

## How it works

1. **Spec-only** (`scenario Foo`) — generates test cases from the scenario, validates that each operation's spec rules produce the expected outcomes against `BankState`. Catches spec bugs without a running service.

2. **Live testing** (`scenario Foo --target Server`) — binds spec operations to HTTP endpoints via `ITestingTarget`, runs the same test cases against a real server, and reports pass/fail with log paths for failures.

3. **Visualize** (`--visualize`) — writes a DOT graph of the scenario's state space to a temp file, useful for debugging spec transitions.

## Resources

- [Accordant GitHub](https://github.com/microsoft/accordant)
- [Accordant Documentation](https://microsoft.github.io/accordant)

## License

MIT
