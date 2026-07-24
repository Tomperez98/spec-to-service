# Spec to Service

Write the spec. Test the spec. Generate the service. Validate everything.

A workflow that flips development: define an executable behavioral specification first, validate it, then build the implementation and mechanically verify conformance.

Powered by [Microsoft Accordant](https://github.com/microsoft/accordant).

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
│   │   ├── ITestScenario.cs    # Test scenario interface
│   │   └── Scenarios/      # Test scenario implementations
│   │       ├── Foo.cs
│   │       └── Bar.cs
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

## Getting Started

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run CLI
dotnet run --project Spec/Cli
```

## Resources

- [Accordant GitHub](https://github.com/microsoft/accordant)
- [Accordant Documentation](https://microsoft.github.io/accordant)

## License

MIT
