import { pgSchema, varchar, decimal } from "drizzle-orm/pg-core";
import { sql } from "drizzle-orm";

// Internal — tables live here, never exposed to PostgREST
export const dataSchema = pgSchema("data");

export const accounts = dataSchema.table("accounts", {
  id: varchar("id").primaryKey(),
});

// Public contract — views + functions only. This is all PostgREST sees.
export const apiSchema = pgSchema("api");

export const accountBalances = apiSchema.view("account_balances", {
  id: varchar("id"),
  balance: decimal("balance"),
}).as(sql`SELECT id, balance FROM data.accounts`);
