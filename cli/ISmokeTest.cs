using Microsoft.Accordant;

namespace Cli.Smokes;

public interface ISmokeTest
{
    static abstract IList<SequentialTestCase> Run();
}
