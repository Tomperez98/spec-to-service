# PostgREST: schema exposure and read/write API design

Notes from reading the PostgREST v14 docs while deciding how to expose our
`schema.ts` (`api` schema, one `accounts` table) through PostgREST.

Sources:
- Tutorial 0: https://docs.postgrest.org/en/v14/tutorials/tut0.html
- Schema Isolation: https://docs.postgrest.org/en/v14/explanations/schema_isolation.html
- API reference: https://docs.postgrest.org/en/v14/references/api.html (+ sub-pages)

## Core model

PostgREST turns a single PostgreSQL schema into a REST API. It exposes exactly
**three** database objects as HTTP resources:

- **tables** and **views** → one-level routes like `/accounts`
- **functions** → `/rpc/<name>`

There are **no nested routes** (`/accounts/1/transactions`); related data is
pulled in via Resource Embedding instead. Each route offers
`GET/HEAD/POST/PATCH/DELETE/OPTIONS` **based entirely on database grants** —
there is no route/controller code to write. The database *is* the API surface.

## Question 1: single `api` schema, or private tables + public views?

Tutorial 0 shows the minimal path: one `api` schema with tables exposed
directly. That is a hello-world, **not** an architecture recommendation.

The recommendation lives on the **Schema Isolation** page, verbatim:

> "It is recommended that you don't expose tables on your API schema. Instead
> expose views and functions which insulate the internal details from the
> outside world."
>
> "This allows you to change the internals of your schema and maintain
> backwards compatibility. It also keeps your code easier to refactor, and
> provides a natural way to do API versioning."

So the documented best practice is **private schema holds tables, `api` schema
holds views/functions**.

### Our decision

Keep the single-schema setup **for now** — one two-column table, no external
clients yet. The private/public split is real best practice but currently
speculative structure. Migration cost later is cheap (`pgSchema` rename + a
view).

Split when the **first** of these happens:
- an external/untrusted client starts hitting the API,
- we need to expose a column subset or a computed/reshaped field,
- a column rename would otherwise break a consumer.

## Question 2: read-only views + all writes via RPC?

Not dogmatically. The split is by **what enforces the invariant**, not a
blanket rule.

- **Reads → views: always.** Cheap, decouples API shape from storage, lets us
  grant `SELECT` on only what should be public.
- **Writes → RPC only when a single-row constraint can't hold the invariant.**

Decision test — *can a DB `CHECK` constraint + RLS enforce the invariant on a
single row?*

- **Yes** → direct table write (`POST`/`PATCH`) is fine and idiomatic. Wrapping
  a plain insert in an RPC just for symmetry is a one-implementation
  abstraction — skip it. (`CHECK (balance >= 0)` stops single-row overdraft.)
- **No — invariant spans rows or needs a transaction** → RPC.

### Why `accounts` balance changes must be RPC

A transfer debits one row and credits another. Two invariants no column
constraint can see:

1. **conservation** — debit must equal credit, and
2. **no lost update** — `PATCH /accounts?id=eq.x` with a new `balance` is a
   client-side read-modify-write; two of them are non-atomic and racy.

So: expose `accounts` reads via a view, do **not** grant `UPDATE` on `balance`,
and move funds through an RPC function that does both writes in one transaction
with row locking. This also matches our `AGENTS.md` rule — invariants validated
at the boundary, a single canonical construction path (the function).

```sql
-- reads: expose a view over a private table, grant SELECT only.

-- writes: one canonical, transactional path. Never grant UPDATE on balance.
-- ponytail: single global function; add per-account advisory locks only if
-- transfer throughput becomes a bottleneck.
create function api.transfer(from_id text, to_id text, amount numeric)
returns void language plpgsql as $$
begin
  if amount <= 0 then raise exception 'amount must be positive'; end if;
  -- lock in a fixed id order to avoid deadlocks
  perform 1 from api.accounts where id in (from_id, to_id) order by id for update;
  update api.accounts set balance = balance - amount
    where id = from_id and balance >= amount;
  if not found then raise exception 'insufficient funds or missing account'; end if;
  update api.accounts set balance = balance + amount where id = to_id;
  if not found then raise exception 'destination account missing'; end if;
end $$;
```

## API reference cheat-sheet (what we get for free)

Things PostgREST already provides, so we never hand-roll them:

- **Horizontal filter (rows):** `?age=lt.13&student=is.true`. Operators:
  `eq gt gte lt lte neq like ilike match in is fts cs cd ov` + range ops.
- **Logical:** `?or=(age.lt.18,age.gt.21)`, negate with `not.`, modifiers
  `like(any).{O*,P*}`.
- **Vertical filter (columns):** `?select=first_name,age`.
- **Resource Embedding (FK joins):** `?select=title,director:directors(id)`.
  Detects M:1 / 1:M / M:M / 1:1 from foreign keys. Needs FKs + schema cache
  reload.
- **Pagination & count:** `?limit=15&offset=30` or `Range` headers; responses
  carry `Content-Range`. `Prefer: count=exact|planned|estimated`.
- **Content negotiation:** `Accept: application/json | text/csv`;
  `application/vnd.pgrst.object+json` → single object (406 if not exactly one);
  `nulls=stripped`.
- **Prefer header behaviors:** `return=minimal|headers-only|representation`,
  `missing=default`, `handling=strict|lenient`, `timezone=`, `max-affected=N`,
  `tx=rollback`.
- **RPC (`/rpc/name`):** any function; POST body args or GET if read-only;
  table-returning functions get the same filters + embedding as tables. This is
  the escape hatch for anything beyond CRUD.
- **Schemas:** single (`db-schemas="api"`) or multiple, switched via
  `Accept-Profile` / `Content-Profile` headers (multi-tenant).
- **Also exist:** Computed Fields, Domain Representations (reshape type
  presentation vs storage), Aggregate Functions (**off by default**), Media Type
  Handlers, auto OpenAPI at `/` (driven by SQL `COMMENT`s), permissive CORS by
  default.
- **URL Grammar limits:** UNION/complex joins/geo-with-args are intentionally
  not expressible in the URL → make a view or function for those.

## Takeaways

1. Tutorial 0's single exposed schema is a demo, not a recommendation; the
   Schema Isolation page recommends private tables + exposed views/functions.
2. Defer the split until there's a real consumer or a reshape/rename need — the
   later migration is cheap.
3. Reads via views always; writes via RPC **only** when a single-row constraint
   can't hold the invariant. Money movement (transfer) is RPC because its
   invariants span rows and need atomicity.
4. Never grant clients `UPDATE` on `balance`; a raw `PATCH` is a lost-update
   race, not a lazy solution.
