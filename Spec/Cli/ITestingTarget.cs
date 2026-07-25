using Microsoft.Accordant;
using Model;

namespace Cli.Targets;

/// <summary>
/// A target that can execute spec operations and reset its state between test cases.
/// </summary>
public interface ITestingTarget
{
    string Name { get; }
    Task ResetAsync();
    void Bind(Spec<BankState> spec, TestingContext context);
}
