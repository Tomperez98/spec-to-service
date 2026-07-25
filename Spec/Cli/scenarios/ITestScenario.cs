using Microsoft.Accordant;
using Model;

namespace Cli.Scenarios;

/// <summary>
/// Implemented by each test scenario so the CLI runner can generate test cases
/// and visualize state spaces without reflection.
/// </summary>
public interface ITestScenario
{
    IList<SequentialTestCase> GenerateTests();
    string VisualizeStateSpace();
    Spec<BankState> GetSpec();
}
