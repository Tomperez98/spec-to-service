CREATE SCHEMA "api";
--> statement-breakpoint
CREATE SCHEMA "data";
--> statement-breakpoint
CREATE TABLE "data"."accounts" (
	"id" varchar PRIMARY KEY,
	"balance" numeric DEFAULT '0' NOT NULL
);
--> statement-breakpoint
CREATE VIEW "api"."account_balances" AS (SELECT id, balance FROM data.accounts);