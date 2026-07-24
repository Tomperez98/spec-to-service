# Transfer derivation was missing `ToAccountId` derivation

## Context

The `Transfer` operation needs two account IDs — `FromAccountId` and
`ToAccountId` — to move funds between accounts. Both IDs come from the
template `TransferRequest("", "", 30m)` whose fields are filled by derivations
from `CreateAccount` responses.

The comment in the spec explicitly describes the intended design:

> _Transfer: each derivation fills its own field, preserving the other from
> the template or prior derivation. The generator composes both._

## The bug

Only **one** derivation was registered for `Transfer` — the one that fills
`FromAccountId`:

```csharp
spec.ConfigureDerivations("Transfer",
    Derive.From<CreateAccountRequest, CreateAccountResponse, TransferRequest>("CreateAccount")
        .When(...)
        .As((_, resp, template) => new TransferRequest(
            ((CreateAccountResponse.Ok)resp).AccountId,  // fills FromAccountId
            template.ToAccountId,                         // stays empty
            template.Amount)));
```

The derivation that fills `ToAccountId` was never written. At execution time,
`ToAccountId` remained the empty string from the template. The Transfer
operation always hit `TransferResponse.TargetNotFound` → `.SameState()`.

## How to detect it

In the DOT state graph (`--visualize`), every Transfer edge was a **self-loop**
— the operation never changed state:

```
"somenode" -> "somenode" [label="[s] ([u] Create target -> Transfer)"];
```

A working Transfer produces edges between *different* nodes whose labels show
the balance shift (e.g., source Balance=70, target Balance=30).

## The fix

Two separate things were needed:

### 1. Add the second derivation

```csharp
Derive.From<CreateAccountRequest, CreateAccountResponse, TransferRequest>("CreateAccount")
    .When(...)
    .As((_, resp, template) => new TransferRequest(
        template.FromAccountId,                          // preserve from prior derivation
        ((CreateAccountResponse.Ok)resp).AccountId,      // fill ToAccountId
        template.Amount))
```

### 2. Pass both derivations in a single `ConfigureDerivations` call

`ConfigureDerivations` accepts `params RequestDerivation[]`. Calling it twice
with the same operation name **replaces** the first derivation rather than
appending. The fix merges them:

```csharp
spec.ConfigureDerivations("Transfer",
    Derive.From<...>("CreateAccount").When(...).As(/* fills FromAccountId */),
    Derive.From<...>("CreateAccount").When(...).As(/* fills ToAccountId   */));
```

The generator composes both derivations from distinct `CreateAccount` responses
against the same template, producing a complete `TransferRequest(sourceId,
targetId, amount)`.

## Result

| Before fix | After fix |
|---|---|
| 100% Transfer edges = self-loops | Non-self-loop Transfer edges exist |
| `TargetNotFound` on every transfer | Valid transfers between distinct accounts |
| No state-changing Transfer in graph | Transfers move funds from source to target |

Smoke test at `MaxDepth=4`: 70 test cases covering all 5 operations.
At `MaxDepth=6`: 2,538 test cases with many successful Transfer paths.

## Files touched

- `model/Spec.cs` — merged two derivations into a single `ConfigureDerivations`
  call for `"Transfer"`, filling both `FromAccountId` and `ToAccountId`.

## Takeaway

When an operation's request has multiple fields that each depend on a different
prior operation's response, register **one derivation per field** and pass them
as separate arguments to a single `ConfigureDerivations` call. Two calls to
`ConfigureDerivations` with the same operation name silently replace each other.
