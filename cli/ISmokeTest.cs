using Microsoft.Accordant;

namespace Cli.Smokes;

/// <summary>
/// Implemented by each smoke test so the CLI runner can generate test cases
/// and visualize state spaces without reflection. Each implementation owns
/// its typed Spec&lt;TState&gt; and calls it directly.
/// </summary>
public interface ISmokeTest
{
    IList<SequentialTestCase> GenerateTests();
    string VisualizeStateSpace();
}
