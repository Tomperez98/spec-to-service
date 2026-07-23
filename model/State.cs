using Microsoft.Accordant;

namespace Model;

[State]
public partial class Account : State
{
    public string Id { get; set; } = "";
    public decimal Balance { get; set; }
}

[State]
public partial class BankState : State
{
    public List<Account> Accounts { get; set; } = [];
}
