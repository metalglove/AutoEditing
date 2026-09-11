using Core.Host.Automation;
using Core.Scripts;

await VegasAutomationBrokerSelfTests.RunAllAsync();
Console.WriteLine("All automation broker self-tests passed.");
PreviewRenderPlanningSelfTests.Run();
Console.WriteLine("All preview-render planning self-tests passed.");
