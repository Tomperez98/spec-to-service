using Microsoft.Accordant;
using Model;

var stepFunctions = BankSpec.CreateStepFunctions();
var initialState = new BankState();

var root = StateGraph.ExploreStateGraph(stepFunctions, initialState, maxDepth: 5,
    true, null, null, null, null);

Console.WriteLine(root.GenerateDotFileContent());
