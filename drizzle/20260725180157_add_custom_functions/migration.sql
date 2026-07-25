-- CreateAccount -> Ok(AccountId). Server generates the id.
create function api.create_account()
returns text language plpgsql as $$
declare new_id text := gen_random_uuid()::text;
begin
  insert into data.accounts (id, balance) values (new_id, 0);
  return new_id;
end $$;
--> statement-breakpoint

-- Deposit -> Ok(NewBalance) | NotFound(404).
create function api.deposit(account_id text, amount numeric)
returns numeric language plpgsql as $$
declare new_balance numeric;
begin
  if amount <= 0 then
    raise exception 'amount must be positive' using errcode = 'PT400';
  end if;
  update data.accounts a set balance = a.balance + amount
    where a.id = account_id
    returning a.balance into new_balance;
  if not found then
    raise exception 'account not found' using errcode = 'PT404';
  end if;
  return new_balance;
end $$;
--> statement-breakpoint

-- Withdraw -> Ok(NewBalance) | NotFound(404) | InsufficientFunds(400).
create function api.withdraw(account_id text, amount numeric)
returns numeric language plpgsql as $$
declare new_balance numeric;
begin
  if amount <= 0 then
    raise exception 'amount must be positive' using errcode = 'PT400';
  end if;
  update data.accounts a set balance = a.balance - amount
    where a.id = account_id and a.balance >= amount
    returning a.balance into new_balance;
  if found then
    return new_balance;
  end if;
  if exists (select 1 from data.accounts where id = account_id) then
    raise exception 'insufficient funds' using errcode = 'PT400';
  else
    raise exception 'account not found' using errcode = 'PT404';
  end if;
end $$;
--> statement-breakpoint

-- CloseAccount -> Ok | NotFound(404) | NonZeroBalance(409).
create function api.close_account(account_id text)
returns void language plpgsql as $$
declare current_balance numeric;
begin
  select balance into current_balance from data.accounts where id = account_id for update;
  if not found then
    raise exception 'account not found' using errcode = 'PT404';
  end if;
  if current_balance <> 0 then
    raise exception 'account balance is % (must be zero to close)', current_balance
      using errcode = 'PT409';
  end if;
  delete from data.accounts where id = account_id;
end $$;
--> statement-breakpoint

-- Transfer -> Ok(FromNewBalance) | SourceNotFound(404) | TargetNotFound(404)
--           | SameAccount(400) | InsufficientFunds(400).
create function api.transfer(from_account_id text, to_account_id text, amount numeric)
returns numeric language plpgsql as $$
declare from_balance numeric;
begin
  if amount <= 0 then
    raise exception 'amount must be positive' using errcode = 'PT400';
  end if;
  if from_account_id = to_account_id then
    raise exception 'cannot transfer to the same account' using errcode = 'PT400';
  end if;

  perform 1 from data.accounts
    where id in (from_account_id, to_account_id) order by id for update;

  if not exists (select 1 from data.accounts where id = from_account_id) then
    raise exception 'source account not found' using errcode = 'PT404';
  end if;
  if not exists (select 1 from data.accounts where id = to_account_id) then
    raise exception 'target account not found' using errcode = 'PT404';
  end if;

  update data.accounts a set balance = a.balance - amount
    where a.id = from_account_id and a.balance >= amount
    returning a.balance into from_balance;
  if not found then
    raise exception 'insufficient funds' using errcode = 'PT400';
  end if;

  update data.accounts set balance = balance + amount where id = to_account_id;
  return from_balance;
end $$;
--> statement-breakpoint

-- Expose read path
grant usage on schema api to web_anon;
grant select on api.account_balances to web_anon;

-- Expose write path
grant execute on function
  api.create_account(),
  api.deposit(text, numeric),
  api.withdraw(text, numeric),
  api.close_account(text),
  api.transfer(text, text, numeric)
to web_anon;
