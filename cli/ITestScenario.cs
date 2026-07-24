using Microsoft.Accordant;

namespace Cli.Scenarios;

/// <summary>
/// Implemented by each test scenario so the CLI runner can generate test cases
/// and visualize state spaces without reflection. Each implementation owns
/// its typed Spec&lt;TState&gt; and calls it directly.
/// </summary>
public interface ITestScenario
{
    IList<SequentialTestCase> GenerateTests();
    string VisualizeStateSpace();
}
