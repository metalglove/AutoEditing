using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.LlmEditor.Inference;
using AutoEditing.LlmEditor.Planning;
using Core.Domain.Planning;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor;

internal static class ProgressiveAssemblyPlannerSelfTests
{
	public static void Run(EditPlanningRequest request)
	{
		Core.Domain.Clip.Clip clip = request.Clips[0];
		string referenceId = AssemblyReferenceIds.ForClipPath(clip.FilePath);
		string eventId = request.SongAnalysis.Events.First(item => item.IsGameplayAnchor).Id;
		string regionId = request.SongAnalysis.Events
			.First(item => item.Id == eventId).ContainingRegionId;
		string sketchJson = ContractSerializer.Serialize(new AssemblySketch
		{
			RequestId = request.RequestId,
			EditorialThesis = "Build clearly toward the strongest reviewed accent.",
			Sections = new[]
			{
				new AssemblySectionIntent
				{
					SectionId = "section-1", RegionId = regionId, EditorialRole = "opening",
					EnergyDirection = "rising", PacingIntent = "readable",
					Rationale = "Establish the rhythm."
				}
			},
			ClipOrder = new[]
			{
				new AssemblyClipIntent
				{
					Order = 1,
					Clip = new AssemblyClipReference
					{
						ReferenceId = referenceId, MediaPath = clip.FilePath
					},
					SectionId = "section-1", EditorialRole = "establish",
					Rationale = "The clip reads clearly.", Confidence = 0.8
				}
			},
			SyncStrategy = new AssemblySyncStrategy
			{
				Density = "sparse", PreferredMusicalTypes = new[] { "Downbeat" },
				Rationale = "Keep anchors legible."
			},
			Reservations = new[]
			{
				new AssemblyReservation
				{
					ReservationId = "reserve-peak", Purpose = "structural peak",
					RegionId = regionId, PreferredClipReferenceIds = new[] { referenceId }
				}
			},
			Uncertainties = new[]
			{
				new AssemblyUncertainty
				{
					UncertaintyId = "uncertainty-trim",
					Description = "The lead-in may be long.",
					ResolutionSignal = "Review the materialized timing."
				}
			}
		});
		double end = Math.Min(clip.DurationSeconds, Math.Max(0.5,
			clip.ConfirmedKills[0].SourceConfirmationTimeSeconds + 0.25));
		string stepJson = ContractSerializer.Serialize(new ClipStepDecision
		{
			RequestId = request.RequestId,
			StepIndex = 1,
			Clip = new AssemblyClipReference { ReferenceId = referenceId, MediaPath = clip.FilePath },
			SourceWindow = new AssemblySourceWindow
			{
				StartSeconds = 0, EndSeconds = end, ConstantSpeed = 1
			},
			PrimarySync = new AssemblySyncDecision { MusicEventId = eventId, KillIndex = 0 },
			Rationale = "The confirmation lands on the reviewed anchor.",
			Alternatives = new[]
			{
				new AssemblyDecisionAlternative
				{
					Description = "Use a later trim.",
					RejectedBecause = "It weakens anticipation."
				}
			},
			Confidence = 0.9
		});
		StubClient client = new(
			"```json\n" + sketchJson + "\n```",
			stepJson,
			stepJson);
		LlmProgressiveAssemblyPlanner planner = new(
			client,
			new InferenceBudgets
			{
				MinimumContextTokens = 4096,
				PlanningMaxOutputTokens = 1024,
				ReviewMaxOutputTokens = 512
			},
			"forensic-test-style-findings");
		AssemblySketch sketch = planner.CreateSketchAsync(
			request, CancellationToken.None).GetAwaiter().GetResult();
		ProgressiveAssemblyPlanningContext context = new()
		{
			RequestId = request.RequestId,
			StepIndex = 1,
			Sketch = sketch,
			RemainingClips = new[]
			{
				new AssemblyClipReference { ReferenceId = referenceId, MediaPath = clip.FilePath }
			},
			NearbySongContext = new[]
			{
				new AssemblySongEventReference
				{
					EventId = eventId,
					EffectiveTimeSeconds = request.SongAnalysis.Events
						.Single(item => item.Id == eventId).EffectiveTimeSeconds,
					RegionId = regionId,
					MusicalType = request.SongAnalysis.Events
						.Single(item => item.Id == eventId).MusicalType.ToString()
				}
			}
		};
		ClipStepDecision step = planner.PlanClipAsync(
			request, context, CancellationToken.None).GetAwaiter().GetResult();
		context.TimelineAdjustment = new TimelineAdjustmentDelta { Checkpoint = 1 };
		context.ScopedInstruction = "Shorten only this clip's lead-in.";
		planner.ReviseClipAsync(request, context, step, CancellationToken.None)
			.GetAwaiter().GetResult();

		Assert(client.Requests.Count == 3, "Progressive planning did not make sketch/step/revision requests.");
		Assert(client.Requests.All(item =>
			item.JsonSchema?.Contains("\"additionalProperties\":false", StringComparison.Ordinal) == true),
			"Progressive planning did not use strict JSON schemas.");
		Assert(client.Requests[0].UserPrompt.Contains("eventTimelineColumns", StringComparison.OrdinalIgnoreCase) &&
			client.Requests[0].UserPrompt.Contains("eventTimeline", StringComparison.OrdinalIgnoreCase),
			"The sketch prompt omitted the complete compact song lattice.");
		Assert(client.Requests[1].UserPrompt.Contains(
				"ACCEPTED PREFIX SUMMARY (AUTHORITATIVE, IMMUTABLE)", StringComparison.Ordinal) &&
			client.Requests[1].UserPrompt.Contains("NEARBY REVIEWED SONG CONTEXT", StringComparison.Ordinal) &&
			client.Requests[1].UserPrompt.Contains(
				"AUTHORITATIVE REMAINING CLIP TIMING", StringComparison.Ordinal) &&
			client.Requests[1].UserPrompt.Contains(
				"sourceConfirmationTimeSeconds", StringComparison.Ordinal) &&
			client.Requests[1].UserPrompt.Contains(
				clip.ConfirmedKills[0].SourceConfirmationTimeSeconds.ToString(
					System.Globalization.CultureInfo.InvariantCulture),
				StringComparison.Ordinal) &&
			client.Requests[1].UserPrompt.Contains(
				"sourceWindow uses source-media seconds", StringComparison.Ordinal),
			"The clip prompt omitted progressive context.");
		Assert(
			client.Requests[1].UserPrompt.Contains(
				"Do not select the whole source clip by default",
				StringComparison.Ordinal) &&
			client.Requests[1].UserPrompt.Contains(
				"normally choose a compact 2-4 second action window",
				StringComparison.Ordinal) &&
			client.Requests[1].UserPrompt.Contains(
				"constantSpeed is the default synchronization velocity",
				StringComparison.Ordinal) &&
			client.Requests[1].UserPrompt.Contains(
				"fast/slow/fast retiming shape",
				StringComparison.Ordinal),
			"The clip prompt omitted compact source-window and velocity-curve guidance.");
		Assert(client.Requests[2].UserPrompt.Contains("Shorten only this clip", StringComparison.Ordinal) &&
			client.Requests[2].UserPrompt.Contains("\"Checkpoint\"", StringComparison.Ordinal),
			"The revision prompt omitted scoped human feedback.");
		Assert(JObject.Parse(client.Requests[0].JsonSchema!)["required"]!.Any(
			item => item!.Value<string>() == "uncertainties"),
			"The sketch schema does not require uncertainties.");
		Assert(
			!client.Requests[1].JsonSchema!.Contains(
				"\"montage\"",
				StringComparison.OrdinalIgnoreCase),
			"A routine clip-step schema still asks the model for an executable montage.");

		AssemblySketch invalidSectionSketch =
			ContractSerializer.Deserialize<AssemblySketch>(sketchJson);
		invalidSectionSketch.ClipOrder[0].SectionId = regionId;
		StubClient sketchRepairClient = new(
			ContractSerializer.Serialize(invalidSectionSketch),
			sketchJson);
		AssemblySketch repairedSketch = new LlmProgressiveAssemblyPlanner(
				sketchRepairClient,
				new InferenceBudgets
				{
					MinimumContextTokens = 4096,
					PlanningMaxOutputTokens = 1024,
					ReviewMaxOutputTokens = 512
				},
				"forensic-test-style-findings")
			.CreateSketchAsync(request, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(
			repairedSketch.ClipOrder[0].SectionId == "section-1" &&
			sketchRepairClient.Requests.Count == 2 &&
			sketchRepairClient.Requests[1].UserPrompt.Contains(
				"AUTOMATIC SKETCH REPAIR",
				StringComparison.Ordinal) &&
			sketchRepairClient.Requests[1].UserPrompt.Contains(
				"references undeclared sectionId",
				StringComparison.Ordinal) &&
			sketchRepairClient.Requests[1].UserPrompt.IndexOf(
				"PREVIOUS REJECTED SKETCH",
				StringComparison.Ordinal) <
			sketchRepairClient.Requests[1].UserPrompt.IndexOf(
				"AUTOMATIC SKETCH REPAIR",
				StringComparison.Ordinal),
			"An invalid sketch section reference was not repaired using its exact " +
			"deterministic diagnostic.");

		string truncatedReferenceId = referenceId[..^1];
		AssemblySketch aliasedSketch =
			ContractSerializer.Deserialize<AssemblySketch>(sketchJson);
		aliasedSketch.ClipOrder[0].Clip.ReferenceId = truncatedReferenceId;
		aliasedSketch.ClipOrder[0].AlternativeClipReferenceIds =
			new[] { truncatedReferenceId };
		aliasedSketch.Reservations[0].PreferredClipReferenceIds =
			new[] { truncatedReferenceId };
		StubClient aliasClient = new(ContractSerializer.Serialize(aliasedSketch));
		AssemblySketch canonicalizedSketch = new LlmProgressiveAssemblyPlanner(
				aliasClient,
				new InferenceBudgets
				{
					MinimumContextTokens = 4096,
					PlanningMaxOutputTokens = 1024,
					ReviewMaxOutputTokens = 512
				},
				"forensic-test-style-findings")
			.CreateSketchAsync(request, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(
			aliasClient.Requests.Count == 1 &&
			canonicalizedSketch.ClipOrder[0].Clip.ReferenceId == referenceId &&
			canonicalizedSketch.ClipOrder[0].AlternativeClipReferenceIds
				.SequenceEqual(new[] { referenceId }) &&
			canonicalizedSketch.Reservations[0].PreferredClipReferenceIds
				.SequenceEqual(new[] { referenceId }),
			"An unknown reference ID paired with its exact authoritative media path " +
			"was not canonicalized consistently before validation.");

		AssemblySketch unknownPathSketch =
			ContractSerializer.Deserialize<AssemblySketch>(sketchJson);
		unknownPathSketch.ClipOrder[0].Clip.ReferenceId = truncatedReferenceId;
		unknownPathSketch.ClipOrder[0].Clip.MediaPath = clip.FilePath + ".unknown";
		string unknownPathJson = ContractSerializer.Serialize(unknownPathSketch);
		ExpectFailure(
			() => new LlmProgressiveAssemblyPlanner(
					new StubClient(
						unknownPathJson,
						unknownPathJson,
						unknownPathJson),
					new InferenceBudgets
					{
						MinimumContextTokens = 4096,
						PlanningMaxOutputTokens = 1024,
						ReviewMaxOutputTokens = 512
					})
				.CreateSketchAsync(request, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"An unknown reference ID paired with an unknown media path was accepted.");
		AssemblySketch missingClipSketch =
			ContractSerializer.Deserialize<AssemblySketch>(sketchJson);
		missingClipSketch.ClipOrder = Array.Empty<AssemblyClipIntent>();
		StubClient missingClipRepairClient = new(
			ContractSerializer.Serialize(missingClipSketch),
			sketchJson);
		new LlmProgressiveAssemblyPlanner(
				missingClipRepairClient,
				new InferenceBudgets
				{
					MinimumContextTokens = 4096,
					PlanningMaxOutputTokens = 1024,
					ReviewMaxOutputTokens = 512
				},
				"forensic-test-style-findings")
			.CreateSketchAsync(request, CancellationToken.None)
			.GetAwaiter().GetResult();
		Assert(
			missingClipRepairClient.Requests.Count == 2 &&
			missingClipRepairClient.Requests[1].UserPrompt.Contains(
				"Missing selected clip " + referenceId,
				StringComparison.Ordinal) &&
			missingClipRepairClient.Requests[1].UserPrompt.Contains(
				Path.GetFileName(clip.FilePath),
				StringComparison.Ordinal) &&
			missingClipRepairClient.Requests[1].UserPrompt.Contains(
				"clipOrder must contain exactly " + request.Clips.Count,
				StringComparison.Ordinal),
			"A missing authoritative clip did not produce precise repair feedback.");

		ExpectFailure(
			() => new LlmProgressiveAssemblyPlanner(
					new StubClient("```json\n{\n```"),
					new InferenceBudgets
					{
						MinimumContextTokens = 4096,
						PlanningMaxOutputTokens = 1024,
						ReviewMaxOutputTokens = 512
					})
				.CreateSketchAsync(request, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"A malformed fenced sketch response was accepted.");
		ExpectFailure(
			() => new LlmProgressiveAssemblyPlanner(
					new StubClient("{}"),
					new InferenceBudgets
					{
						MinimumContextTokens = 4096,
						PlanningMaxOutputTokens = 1024,
						ReviewMaxOutputTokens = 512
					})
				.CreateSketchAsync(request, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"An incomplete but parseable sketch response was accepted.");
		ClipStepDecision wrongStep =
			ContractSerializer.Deserialize<ClipStepDecision>(stepJson);
		wrongStep.StepIndex = 2;
		ExpectFailure(
			() => new LlmProgressiveAssemblyPlanner(
					new StubClient(ContractSerializer.Serialize(wrongStep)),
					new InferenceBudgets
					{
						MinimumContextTokens = 4096,
						PlanningMaxOutputTokens = 1024,
						ReviewMaxOutputTokens = 512
					})
				.PlanClipAsync(request, context, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"A semantically invalid clip-step response targeting another checkpoint was accepted.");

		ClipStepDecision excludedKill =
			ContractSerializer.Deserialize<ClipStepDecision>(stepJson);
		double killTime = clip.ConfirmedKills[0].SourceConfirmationTimeSeconds;
		if (killTime > 0.02)
		{
			excludedKill.SourceWindow.StartSeconds = 0;
			excludedKill.SourceWindow.EndSeconds = killTime / 2;
		}
		else
		{
			excludedKill.SourceWindow.StartSeconds = Math.Min(
				clip.DurationSeconds - 0.02, killTime + 0.01);
			excludedKill.SourceWindow.EndSeconds = Math.Min(
				clip.DurationSeconds, excludedKill.SourceWindow.StartSeconds + 0.01);
		}
		ExpectFailure(
			() => new LlmProgressiveAssemblyPlanner(
					new StubClient(ContractSerializer.Serialize(excludedKill)),
					new InferenceBudgets
					{
						MinimumContextTokens = 4096,
						PlanningMaxOutputTokens = 1024,
						ReviewMaxOutputTokens = 512
					})
				.PlanClipAsync(request, context, CancellationToken.None)
				.GetAwaiter().GetResult(),
			"A clip step whose source window excludes its selected kill was accepted.");
	}

	private static void Assert(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	private static void ExpectFailure(Action action, string message)
	{
		try { action(); }
		catch { return; }
		throw new InvalidOperationException(message);
	}

	private sealed class StubClient : ITextGenerationClient
	{
		private readonly Queue<string> responses;
		public List<TextGenerationRequest> Requests { get; } = new();
		public StubClient(params string[] responses) => this.responses = new Queue<string>(responses);
		public Task<TextGenerationResult> GenerateAsync(
			TextGenerationRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(new TextGenerationResult
			{
				Text = responses.Dequeue(), Model = "test"
			});
		}
	}
}
