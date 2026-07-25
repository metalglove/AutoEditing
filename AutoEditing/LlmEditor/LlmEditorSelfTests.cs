using Core.Domain.Planning;

namespace AutoEditing.LlmEditor;

internal static class LlmEditorSelfTests
{
	public static int Run()
	{
		string root = Path.Combine(Path.GetTempPath(), "AutoEditing.LlmEditor.Tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			EditPlanningRequest request = EditPlanDocumentSerializer.DeserializeRequest(ValidRequestJson());
			FakeLlmEditPlanner planner = new FakeLlmEditPlanner();
			EditPlanDocument first = planner.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult();
			EditPlanDocument second = planner.CreatePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult();
			string firstJson = EditPlanDocumentSerializer.SerializePlan(first);
			string secondJson = EditPlanDocumentSerializer.SerializePlan(second);
			Assert(firstJson == secondJson, "The fake planner output is not deterministic.");

			string firstPath = Path.Combine(root, "first.json");
			string secondPath = Path.Combine(root, "second.json");
			EditPlanDocumentSerializer.WritePlanNew(firstPath, first);
			EditPlanDocumentSerializer.WritePlanNew(secondPath, second);
			Assert(File.ReadAllBytes(firstPath).SequenceEqual(File.ReadAllBytes(secondPath)),
				"Serialized plan bytes differ across identical runs.");

			EditPlanDocument roundTripped = EditPlanDocumentSerializer.ReadPlan(firstPath);
			Assert(roundTripped.RequestId == request.RequestId, "Request correlation did not survive serialization.");
			Assert(roundTripped.Montage.Placements.Count == 1, "The round-tripped plan lost its placement.");

			ExpectFailure(
				() => EditPlanDocumentSerializer.DeserializeRequest(
					ValidRequestJson().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2")),
				"An unsupported request schema was accepted.");

			Console.WriteLine("LLM editor skeleton self-tests passed.");
			return 0;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
		finally
		{
			try { if (Directory.Exists(root)) Directory.Delete(root, true); }
			catch { }
		}
	}

	private static void ExpectFailure(Action action, string failureMessage)
	{
		try
		{
			action();
		}
		catch
		{
			return;
		}
		throw new InvalidOperationException(failureMessage);
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static string ValidRequestJson()
	{
		return """
		{
		  "schemaVersion": 1,
		  "requestId": "skeleton-self-test",
		  "clips": [
		    {
		      "filePath": "fixtures/clip-001.mp4",
		      "durationSeconds": 4.0,
		      "shotEvents": []
		    }
		  ],
		  "songPath": "fixtures/song.wav",
		  "effectOptions": {
		    "schemaVersion": 1,
		    "presetId": "autoediting.none",
		    "intensity": 1.0,
		    "density": 1.0,
		    "includeManualTreatments": true,
		    "enableScreenPumps": false,
		    "enableFlashes": false,
		    "enableShake": false,
		    "enableTransitions": false,
		    "enableTitles": false,
		    "enableSpeedChanges": false
		  },
		  "creativeBrief": "Contract-only deterministic skeleton.",
		  "styleProfileIds": ["editor-1"]
		}
		""";
	}
}
