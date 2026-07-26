using Core.Domain.Planning;
using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Planning;
using Newtonsoft.Json;

namespace AutoEditing.LlmEditor;

internal static class Program
{
	private const int Success = 0;
	private const int UnexpectedFailure = 1;
	private const int UsageOrValidationFailure = 2;

	private static async Task<int> Main(string[] args)
	{
		if (args.Length == 1 && args[0] == "--self-test")
			return LlmEditorSelfTests.Run();

		if (args.Length != 7 || args[0] != "plan" || args[1] != "--request" ||
			args[3] != "--output" || args[5] != "--planner")
		{
			PrintUsage();
			return UsageOrValidationFailure;
		}
		try
		{
			EditPlanningRequest request = EditPlanDocumentSerializer.ReadRequest(args[2]);
			IEditPlanner planner = CreatePlanner(args[6]);
			EditPlanDocument document = await planner.CreatePlanAsync(request, CancellationToken.None);
			if (!string.Equals(document.RequestId, request.RequestId, StringComparison.Ordinal))
				throw new InvalidOperationException("The edit plan request ID does not match its planning request.");
			EditPlanDocumentSerializer.WritePlanNew(args[4], document);
			Console.WriteLine("Wrote validated edit plan: " + Path.GetFullPath(args[4]));
			return Success;
		}
		catch (Exception exception) when (
			exception is JsonException ||
			exception is IOException ||
			exception is InvalidDataException ||
			exception is InvalidOperationException ||
			exception is ArgumentException ||
			exception is NotSupportedException)
		{
			Console.Error.WriteLine(exception.Message);
			return UsageOrValidationFailure;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return UnexpectedFailure;
		}
	}

	private static IEditPlanner CreatePlanner(string planner)
	{
		if (string.Equals(planner, "fake", StringComparison.Ordinal))
			return new FakeLlmEditPlanner();
		if (string.Equals(planner, "local", StringComparison.Ordinal))
		{
			OpenAiCompatibleOptions options = OpenAiCompatibleOptions.FromEnvironment();
			return new LlmEditPlanner(
				new OpenAiCompatibleTextGenerationClient(new HttpClient(), options));
		}

		throw new ArgumentException(
			"Unknown planner '" + planner + "'. Expected 'fake' or 'local'.");
	}

	private static void PrintUsage()
	{
		Console.Error.WriteLine(
			"Usage: AutoEditing.LlmEditor plan --request <request.json> --output <plan.json> " +
			"--planner <fake|local>");
	}
}
