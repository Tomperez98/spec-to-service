using Microsoft.Accordant;

namespace Model;

[State]
public partial class BankState : State
{
    public Dictionary<string, decimal> Accounts { get; set; } = new();
}
