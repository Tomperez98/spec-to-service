# `GenerateTests` without a server — what it does and doesn't prove

## The question

If there's no implementation to run generated tests against, is
`spec.GenerateTests` doing anything useful? And can `Derive.From` even work
when the responses it feeds off don't come from a real server?

## Short answer

Yes on both counts, but the value shifts from "catches implementation bugs"
to "catches **spec bugs** via invariants over the generated state graph." And
for a spec with server‑generated IDs, `Derive.From` isn't optional — it's the
only way to get a graph worth checking.

## What `GenerateTests` actually does without a server

The generator walks the state graph defined by the spec:

1. Pick an operation from the input set.
2. Call the spec's `Apply` to get an `Expect + ThenState`.
3. If `ThenState` is response‑dependent, invoke its `mock` to synthesize a
   plausible response.
4. Advance state with that synthesized response.
5. Recurse from the new state until `MaxDepth` or `StateConstraint` cuts it.

Every response along the way is spec‑synthesized. No server needed.

## What that buys you

- **Invariant checking on the generated state graph.** Walk every reachable
  state and assert properties the spec must preserve: no negative balances,
  no duplicate account IDs, closed accounts don't reappear, sum of balances
  is conserved across Transfer. Violations mean the *spec* is wrong. This is
  a real check with no server involved.
- **Visualization / coverage.** Dump the graph. See what the model admits.
  Often reveals accidental reachability ("wait, we allow closing an account
  that just received a transfer?") or missing branches.
- **Pre‑generated test cases.** Serialize the sequences now, execute them
  later once an implementation exists.

## What it does not buy you

- **Implementation bugs.** There is no implementation to be buggy.
- **Oracle validation of expected responses.** The "expected" responses in the
  generated cases came from the spec's own mocks. Running those against the
  spec is tautological. They only become an oracle when executed against a
  real system via `.Bind` + `RunTests`.

## Can `Derive.From` work without a server? Yes.

`Derive.From` operates on whatever request/response pair the framework has
recorded for the source operation. During generation that pair is
(request built by generator, response synthesized by mock). The derivation
function doesn't know or care that the response came from a mock.

Pipeline for `CreateAccount → Deposit` without a server:

1. Generator applies `CreateAccount` from empty state.
2. `ThenState`'s `mock` returns `CreateAccountResponse.Ok(newGuid)`.
3. State advances: `Accounts += { Id = newGuid, Balance = 0 }`.
4. Generator wants to try `Deposit`.
5. `Derive.From<CreateAccountRequest, CreateAccountResponse, DepositRequest>("CreateAccount")`
   reads the mocked response and produces `new DepositRequest(newGuid, amount)`.
6. `Deposit`'s `Apply` runs with that request. It finds the account. Returns
   `Ok(amount)`. State advances again.

All spec‑synthesized. All meaningful for invariant checking.

## Why derivations are *essential* for the bank spec, not merely useful

The old `InputSet` used hardcoded IDs (`"alice"`, `"bob"`) in Deposit /
Transfer / GetBalance / CloseAccount inputs. With `CreateAccount` now
returning a server‑generated Guid, those literals will **never** match any
account the generator creates. Consequences without derivations:

- Every non‑create op self‑loops as `NotFound` from every reachable state.
- The generated graph is `CreateAccount` transitions with no downstream use.
- Zero interesting sequences. Zero interesting invariants to check.

With derivations, downstream ops receive the ID that was just minted, so the
generator can actually explore `Create → Deposit → Transfer → Close` chains
and produce a state graph worth walking.

## Sketch of a `FuzzTests` that would earn its keep

1. Add `Derive.From(...)` for each op needing an `AccountId`:
   - `Deposit`, `Withdraw`, `GetBalance`, `CloseAccount`:
     derive `AccountId` from `CreateAccount`'s response.
   - `Transfer`: derive `FromAccountId` and `ToAccountId` from two different
     `CreateAccount` responses (variants / templates for the amount).
2. Rebuild the `InputSet` around `CreateAccount` plus templates for amounts
   and transfer pairings — no hardcoded IDs.
3. Call `GenerateTests` and walk the resulting graph, asserting:
   - `Accounts.All(a => a.Balance >= 0)`
   - `Accounts.Select(a => a.Id).Distinct().Count() == Accounts.Count`
   - After a `Transfer`, sum of balances equals the sum before the transfer.
   - `CloseAccount(Ok)` transitions never leave a residual account with the
     same ID in state.

A violation of any of those points at a bug in `model/Spec.cs`, not in
tests, not in a server. That's the shape of a fuzz test that pays for itself
before there's a system under test.

## Rule of thumb

| Have a server? | `GenerateTests` role | What catches bugs |
|---|---|---|
| No  | Oracle for the spec itself | Invariant checks over generated state graph |
| Yes | Oracle for the implementation | `.Bind` + `RunTests` diffs spec vs. system |

Either way, the spec is doing work. Without a server, it's checking itself
for internal consistency; with a server, it's checking the system for
conformance.
