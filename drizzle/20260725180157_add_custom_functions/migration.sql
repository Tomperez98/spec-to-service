CREATE ROLE web_anon nologin;

GRANT usage ON schema api TO web_anon;

GRANT
SELECT
  ON api.account_balances TO web_anon;

-- CreateAccount -> Ok(AccountId). Server generates the id.
CREATE FUNCTION api.create_account () returns TEXT language plpgsql AS $$
declare new_id text := uuidv7()::text;
begin
  insert into data.accounts (id, balance) values (new_id, 0);
  return new_id;
end $$;

-- Deposit -> Ok(NewBalance) | NotFound(404).
CREATE FUNCTION api.deposit (account_id TEXT, amount NUMERIC) returns NUMERIC language plpgsql AS $$
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

-- Withdraw -> Ok(NewBalance) | NotFound(404) | InsufficientFunds(400).
CREATE FUNCTION api.withdraw (account_id TEXT, amount NUMERIC) returns NUMERIC language plpgsql AS $$
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

-- CloseAccount -> Ok | NotFound(404) | NonZeroBalance(409).
CREATE FUNCTION api.close_account (account_id TEXT) returns void language plpgsql AS $$
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

-- Transfer -> Ok(FromNewBalance) | SourceNotFound(404) | TargetNotFound(404)
--           | SameAccount(400) | InsufficientFunds(400).
CREATE FUNCTION api.transfer (
  from_account_id TEXT,
  to_account_id TEXT,
  amount NUMERIC
) returns NUMERIC language plpgsql AS $$
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

-- Expose read path
GRANT usage ON schema api TO web_anon;

GRANT
SELECT
  ON api.account_balances TO web_anon;

-- Expose write path
GRANT
EXECUTE ON function api.create_account (),
api.deposit (TEXT, NUMERIC),
api.withdraw (TEXT, NUMERIC),
api.close_account (TEXT),
api.transfer (TEXT, TEXT, NUMERIC) TO web_anon;
