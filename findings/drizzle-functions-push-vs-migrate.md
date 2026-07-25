# Drizzle: PL/pgSQL functions, and why `db:push` won't apply them

Notes from wiring the PostgREST RPC functions (see
`postgrest-api-and-schema-design.md`) into this repo's Drizzle setup
(`drizzle-kit` 1.0 rc, `out: ./drizzle`, `schema: ./db/schema.ts`).

## What Drizzle can and cannot model

Drizzle's TS schema (`db/schema.ts`) models **tables, columns, schemas, views,
enums, indexes** — objects it can diff. It does **not** have a builder for a
PL/pgSQL function body. So our five bank operations (`create_account`,
`deposit`, `withdraw`, `close_account`, `transfer`) can only live as raw SQL in
a **custom migration**, never in `db/schema.ts`.

Create a custom (empty) migration and hand-write the SQL into it:

```bash
bun run db:generate --custom --name=api_functions
# fills drizzle/<timestamp>_api_functions/migration.sql with a stub;
# paste the CREATE FUNCTION statements there, separated by --> statement-breakpoint
```

## Why `bun run db:push` does NOT add the functions

`push` and migration files are two separate worlds that never touch:

| Command | Reads | Applies |
|---|---|---|
| `db:push` | `db/schema.ts` **only** | Drizzle-modeled objects (tables/columns/views/enums), diffed straight to the DB |
| `db:migrate` | `drizzle/*/migration.sql` files | everything in those files, incl. custom function SQL |

- `push` **never reads `drizzle/*/migration.sql`**. It diffs TS-schema-vs-live-DB
  and applies the difference directly.
- The functions aren't in `db/schema.ts` (Drizzle can't represent them), so they
  appear on neither side of that diff. `push` has nothing to push.

To get the functions into the DB, run **`bun run db:migrate`**, not `push`.
`migrate` is what executes the custom migration file.

## The footgun: don't mix `push` and `generate`/`migrate`

`generate` builds migrations from **TS schema + prior migration snapshots** — it
**never inspects the live DB**. Symptoms we hit in this repo:

- `api.accounts` already existed in the DB, created by an earlier `db:push`.
- `drizzle.__drizzle_migrations` existed but was **empty** (push records nothing).
- A fresh `db:generate` therefore emitted a full `CREATE SCHEMA "api"` +
  `CREATE TABLE` migration — it had no snapshot and can't see the live DB, so
  from its view this was migration #1. Running `migrate` on that would fail with
  "schema api already exists".

### Rule

Pick ONE workflow and stick to it:

- **`push`** = throwaway prototyping. No migration files, no history.
- **`generate` + `migrate`** = versioned migrations. This is what we use, because
  functions/roles/grants can only be delivered as custom SQL migrations anyway.

If a DB was built by `push` and you switch to migrations, you must reconcile:
either reset the DB and `migrate` from scratch, or baseline (mark the
table-creating migration as already applied) so `migrate` starts at the custom
function migration.

## Functions still need grants

Creating the functions does not expose them. PostgREST only serves functions the
active role may execute:

```sql
grant execute on function
  api.create_account(),
  api.deposit(text, numeric),
  api.withdraw(text, numeric),
  api.close_account(text),
  api.transfer(text, text, numeric)
to web_anon;
```

Roles and grants, like functions, are not Drizzle-modeled — they belong in a
custom migration (or out-of-band for anything with a password, e.g. the
`authenticator` login role).

## Takeaways

1. PL/pgSQL functions can't live in `db/schema.ts`; deliver them via
   `db:generate --custom` + hand-written SQL, applied by `db:migrate`.
2. `db:push` reads only the TS schema and never runs migration files, so it can
   never apply functions/roles/grants. Use `db:migrate`.
3. Never mix `push` with `generate`/`migrate` — `generate` ignores the live DB,
   so a push-built DB and generated migrations drift apart immediately.
4. Functions need `GRANT EXECUTE ... TO web_anon` before PostgREST exposes them.
