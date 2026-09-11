using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoEditing.Iteration.Contracts;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Configuration;
using AutoEditing.Iteration.Contracts.Evidence;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using Newtonsoft.Json.Linq;

Run("inference provider settings persist without secrets", () =>
{
	string root = Path.Combine(
		Path.GetTempPath(),
		"AutoEditing-InferenceSettings-" + Guid.NewGuid().ToString("N"));
	string path = Path.Combine(root, "inference.json");
	try
	{
		InferenceProviderSettingsStore.Save(
			new InferenceProviderSettings
			{
				Provider = InferenceProviderIds.OpenAi,
				LlamaCppEndpoint = "http://localhost:8080/v1",
				LlamaCppModel = "local-model",
				OpenAiEndpoint = "https://api.openai.com/v1",
				OpenAiModel = "gpt-test",
				OpenAiCredentialTarget = "AutoEditing/Test/OpenAI"
			},
			path);
		InferenceProviderSettings loaded =
			InferenceProviderSettingsStore.LoadOrDefault(path);
		string json = File.ReadAllText(path);
		Assert(
			loaded.Provider == InferenceProviderIds.OpenAi &&
			loaded.LlamaCppEndpoint == "http://localhost:8080/v1/" &&
			loaded.OpenAiEndpoint == "https://api.openai.com/v1/" &&
			loaded.OpenAiModel == "gpt-test");
		Assert(
			json.IndexOf("apiKey", StringComparison.OrdinalIgnoreCase) < 0 &&
			json.IndexOf("secret", StringComparison.OrdinalIgnoreCase) < 0 &&
			json.IndexOf("sk-", StringComparison.OrdinalIgnoreCase) < 0);
	}
	finally
	{
		if (Directory.Exists(root))
			Directory.Delete(root, true);
	}
});

Run("inference provider settings reject invalid providers and endpoints", () =>
{
	Expect<InvalidDataException>(() =>
		new InferenceProviderSettings
		{
			Provider = "unknown"
		}.ValidateAndNormalize());
	Expect<InvalidDataException>(() =>
		new InferenceProviderSettings
		{
			LlamaCppEndpoint = "file:///not-an-api"
		}.ValidateAndNormalize());
});

Run("session transitions", () =>
{
	Assert(EditSessionStateTransitionValidator.CanTransition(EditSessionState.Created, EditSessionState.Planning));
	Assert(!EditSessionStateTransitionValidator.CanTransition(EditSessionState.Completed, EditSessionState.Planning));
	Assert(EditSessionStateTransitionValidator.CanTransition(
		EditSessionState.Paused, EditSessionState.Polishing));
	Assert(EditSessionStateTransitionValidator.CanTransition(
		EditSessionState.Paused, EditSessionState.FinalReview));
	Expect<InvalidOperationException>(() =>
		EditSessionStateTransitionValidator.Validate(EditSessionState.Completed, EditSessionState.Planning));
});

Run("assembly lifecycle follows the runtime lease", () =>
{
	AssemblyLifecycleAvailability liveReview = AssemblyLifecyclePolicy.Evaluate(
		EditSessionState.AwaitingUser,
		AssemblyPhase.AwaitingHumanReview,
		checkpoint: 3,
		stateRevision: 9,
		hasLiveConsumer: true);
	Assert(liveReview.CanPause && liveReview.CanAbandon && !liveReview.CanResume);

	AssemblyLifecycleAvailability livePaused = AssemblyLifecyclePolicy.Evaluate(
		EditSessionState.Paused,
		AssemblyPhase.Paused,
		checkpoint: 3,
		stateRevision: 10,
		hasLiveConsumer: true);
	Assert(livePaused.CanResume && !livePaused.CanPause && livePaused.CanAbandon);

	AssemblyLifecycleAvailability liveRoughCut = AssemblyLifecyclePolicy.Evaluate(
		EditSessionState.AwaitingUser,
		AssemblyPhase.RoughCutReview,
		checkpoint: 4,
		stateRevision: 19,
		hasLiveConsumer: true);
	Assert(
		liveRoughCut.CanPause &&
		liveRoughCut.CanAbandon &&
		!liveRoughCut.CanResume);

	AssemblyLifecycleAvailability stopped = AssemblyLifecyclePolicy.Evaluate(
		EditSessionState.Failed,
		AssemblyPhase.CreatingSketch,
		checkpoint: 0,
		stateRevision: 1,
		hasLiveConsumer: false);
	Assert(stopped.CanResume && !stopped.CanPause && stopped.CanAbandon);

	AssemblyLifecycleAvailability stoppedTerminal = AssemblyLifecyclePolicy.Evaluate(
		EditSessionState.Accepted,
		AssemblyPhase.SyncPassComplete,
		checkpoint: 3,
		stateRevision: 11,
		hasLiveConsumer: false);
	Assert(!stoppedTerminal.CanResume &&
		!stoppedTerminal.CanPause &&
		!stoppedTerminal.CanAbandon);
});

Run("session IDs are path-safe and workspace-compatible", () =>
{
	foreach (string value in new[]
		{
			"edit-20260727-120000",
			"recovery_session_1",
			"A"
		})
		EditSessionIdValidator.Validate(value);
	foreach (string value in new[]
		{
			"../outside",
			"with spaces",
			"-starts-with-symbol",
			new string('a', 65)
		})
		Expect<ArgumentException>(() => EditSessionIdValidator.Validate(value));
});

Run("candidate placement identity survives timeline edits", () =>
{
	CandidateWorkspaceId workspace = new()
	{
		SessionId = "identity-session",
		Iteration = 2,
		Nonce = "candidate"
	};
	string first = CandidatePlacementIdentity.Create(
		workspace, 3, Path.GetFullPath("fixtures/clip.mp4"));
	string second = CandidatePlacementIdentity.Create(
		workspace, 3, Path.GetFullPath("fixtures/clip.mp4"));
	Assert(first == second &&
		CandidatePlacementIdentity.IsOwned(workspace, first));
	Assert(!CandidatePlacementIdentity.IsOwned(
		new CandidateWorkspaceId
		{
			SessionId = "other-session",
			Iteration = 2,
			Nonce = "candidate"
		},
		first));
});

Run("audio pass reuses only the exact synchronization song", () =>
{
	string songPath = Path.GetFullPath("fixtures/song.wav");
	AudioPassSongAction planned = new()
	{
		SongPath = songPath,
		TimelineStartSeconds = 0,
		TrackGain = 0.5
	};
	CandidateTrackSnapshot track = new()
	{
		MediaKind = "Audio",
		Events = new List<CandidateEventSnapshot>
		{
			new()
			{
				MediaPath = songPath,
				TimelineStart = TimeSpan.Zero,
				Gain = 0.5
			}
		}
	};
	CandidateAudioReuseContract.ValidateSongTrack(track, planned);
	track.Events[0].Gain = 0.7;
	Expect<InvalidOperationException>(() =>
		CandidateAudioReuseContract.ValidateSongTrack(track, planned));
});

Run("canonical hashes", () =>
{
	Assert(ContractHash.Compute(JObject.Parse("""{"b":2,"a":{"d":4,"c":3}}""")) ==
		ContractHash.Compute(JObject.Parse("""{"a":{"c":3,"d":4},"b":2}""")));
});

Run("canonical hashes survive numeric payload round trip", () =>
{
	JToken authored = JToken.FromObject(new
	{
		TimelineStartSeconds = 0.0,
		DurationSeconds = 14.4213333,
		Velocity = new[] { 2.87, 0.5, 2.76, 2.8416667, 1.6166666999999997 },
		Count = 11,
		Label = "```json is transport text, not payload"
	});
	string before = ContractHash.Compute(authored);
	JToken transported = JToken.Parse(authored.ToString(Newtonsoft.Json.Formatting.Indented));
	string after = ContractHash.Compute(transported);
	Assert(before == after);
});

Run("envelope round trip and validation", () =>
{
	DateTimeOffset now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
	JObject payload = JObject.Parse("""{"workspace":{"sessionId":"session-1","iteration":1,"nonce":"abc"}}""");
	VegasJobEnvelope source = new()
	{
		SessionId = "session-1",
		JobId = "job-1",
		Sequence = 3,
		IdempotencyKey = "session-1.job-1",
		Operation = VegasOperations.GetCandidateSnapshot,
		CreatedUtc = now,
		DeadlineUtc = now.AddMinutes(1),
		Payload = payload,
		PayloadSha256 = ContractHash.Compute(payload)
	};
	VegasJobEnvelope copy = ContractSerializer.DeserializeAndValidateEnvelope(
		ContractSerializer.Serialize(source), now);
	Assert(copy.JobId == source.JobId && copy.PayloadSha256 == source.PayloadSha256);
});

Run("unknown schema and members rejected", () =>
{
	VegasJobEnvelope envelope = ValidEnvelope();
	envelope.SchemaVersion = ContractSchema.CurrentVersion + 1;
	Expect<InvalidOperationException>(() => VegasContractValidator.Validate(envelope, envelope.CreatedUtc));
	JObject unknown = JObject.Parse(ContractSerializer.Serialize(ValidEnvelope()));
	unknown["unexpected"] = true;
	string json = unknown.ToString();
	Expect<Newtonsoft.Json.JsonSerializationException>(() =>
		ContractSerializer.Deserialize<VegasJobEnvelope>(json));
});

Run("payload tampering rejected", () =>
{
	VegasJobEnvelope envelope = ValidEnvelope();
	envelope.Payload["changed"] = true;
	Expect<InvalidOperationException>(() => VegasContractValidator.Validate(envelope, envelope.CreatedUtc));
});

Run("path traversal rejected", () =>
{
	foreach (string path in new[] { "../preview.mp4", "frames/../secret", @"C:\secret", "/root/secret" })
		Expect<ArgumentException>(() => SafeRelativePath.Validate(path, "path"));

	EditEvidenceReference evidence = new()
	{
		EvidenceId = "e1",
		RelativePath = "artifacts/frames/001.png",
		TimelineStart = TimeSpan.Zero,
		TimelineEnd = TimeSpan.FromSeconds(1)
	};
	VegasContractValidator.Validate(evidence);
	Assert(evidence.RelativePath == "artifacts/frames/001.png");
});

Run("workspace and preview validation", () =>
{
	CandidateWorkspaceId workspace = new() { SessionId = "session_1", Iteration = 2, Nonce = "n-1" };
	workspace.Validate();
	Assert(workspace.OwnershipPrefix == "AE|LLM|session_1|0002|n-1");
	Expect<ArgumentException>(() => new CandidateWorkspaceId
		{ SessionId = "../bad", Iteration = 1, Nonce = "x" }.Validate());

	RenderCandidatePreviewRequest request = new()
	{
		Workspace = workspace,
		Start = TimeSpan.Zero,
		Duration = TimeSpan.FromSeconds(10),
		RenderProfileId = "review-1080p",
		OutputRelativePath = @"iterations\0002\artifacts\preview.mp4"
	};
	VegasContractValidator.Validate(request);
	Assert(request.OutputRelativePath == "iterations/0002/artifacts/preview.mp4");

	CaptureCandidatePreviewFramesRequest frames = new()
	{
		Workspace = workspace,
		TimelineTimes = new[]
		{
			TimeSpan.FromSeconds(1),
			TimeSpan.FromSeconds(2),
			TimeSpan.FromSeconds(3)
		},
		OutputDirectoryRelativePath = @"iterations\0002\artifacts\frames"
	};
	VegasContractValidator.Validate(frames);
	Assert(frames.OutputDirectoryRelativePath ==
		"iterations/0002/artifacts/frames");
	frames.TimelineTimes = new[]
	{
		TimeSpan.FromSeconds(2),
		TimeSpan.FromSeconds(2)
	};
	Expect<ArgumentException>(() => VegasContractValidator.Validate(frames));

	MaterializeCandidateRequest materialize = new()
	{
		Workspace = workspace,
		Plan = new Core.Domain.Planning.EditPlanDocument(),
		SongPath = Path.GetFullPath("song.wav")
	};
	VegasContractValidator.Validate(materialize);
	materialize.SongPath = "relative-song.wav";
	Expect<ArgumentException>(() => VegasContractValidator.Validate(materialize));
});

Run("candidate promotion is rename-only and collision-safe", () =>
{
	CandidateWorkspaceId workspace = new()
		{ SessionId = "session_1", Iteration = 2, Nonce = "n-1" };
	CandidateTimelineSnapshot snapshot = new()
	{
		Workspace = workspace,
		Tracks = new[]
		{
			new CandidateTrackSnapshot
			{
				Index = 0,
				Name = workspace.OwnershipPrefix + "|VIDEO",
				MediaKind = "Video"
			},
			new CandidateTrackSnapshot
			{
				Index = 1,
				Name = workspace.OwnershipPrefix + "|SONG",
				MediaKind = "Audio"
			},
			new CandidateTrackSnapshot
			{
				Index = 2,
				Name = workspace.OwnershipPrefix + "|SFX|01",
				MediaKind = "Audio"
			}
		}
	};
	IList<CandidatePromotionTrackMapping> mapping =
		CandidatePromotionContract.Plan(snapshot, new[] { "Unrelated user track" });
	Assert(mapping.Count == 3);
	Assert(mapping.Single(item => item.MediaKind == "Video").FinalName ==
		CandidatePromotionContract.FinalVideoTrackName);
	Assert(mapping.All(item =>
		item.CandidateName.StartsWith(workspace.OwnershipPrefix + "|",
			StringComparison.Ordinal)));
	Expect<InvalidOperationException>(() =>
		CandidatePromotionContract.Plan(
			snapshot,
			new[] { CandidatePromotionContract.FinalVideoTrackName }));

	CandidateTimelineSnapshot unknown = ContractSerializer
		.Deserialize<CandidateTimelineSnapshot>(ContractSerializer.Serialize(snapshot));
	unknown.Tracks[2].Name = workspace.OwnershipPrefix + "|UNKNOWN";
	Expect<InvalidOperationException>(() =>
		CandidatePromotionContract.Plan(unknown, Array.Empty<string>()));

	PromoteCandidateResult invalid = new()
	{
		PromotionId = "promotion-1",
		Workspace = workspace,
		CandidateSnapshot = snapshot,
		CandidateSnapshotSha256 = "candidate",
		PromotedSnapshot = snapshot,
		PromotedSnapshotSha256 = "promoted",
		TrackMappings = mapping
	};
	Expect<InvalidOperationException>(() =>
		CandidatePromotionContract.ValidateResult(invalid));

	CandidateTimelineSnapshot promoted = ContractSerializer
		.Deserialize<CandidateTimelineSnapshot>(ContractSerializer.Serialize(snapshot));
	foreach (CandidatePromotionTrackMapping item in mapping)
		promoted.Tracks.Single(track => track.Name == item.CandidateName).Name =
			item.FinalName;
	PromoteCandidateResult valid = new()
	{
		PromotionId = "promotion-2",
		Workspace = workspace,
		CandidateSnapshot = snapshot,
		CandidateSnapshotSha256 = ContractHash.Compute(JToken.FromObject(snapshot)),
		PromotedSnapshot = promoted,
		PromotedSnapshotSha256 = ContractHash.Compute(JToken.FromObject(promoted)),
		TrackMappings = mapping
	};
	CandidatePromotionContract.ValidateResult(valid);
	VegasContractValidator.Validate(new PromoteCandidateRequest
	{
		PromotionId = valid.PromotionId,
		Workspace = workspace,
		ExpectedCandidateSnapshotSha256 = valid.CandidateSnapshotSha256
	});
	VegasContractValidator.Validate(new RollbackCandidatePromotionRequest
	{
		Promotion = valid
	});
	Expect<ArgumentException>(() =>
		VegasContractValidator.Validate(new PromoteCandidateRequest
		{
			PromotionId = "promotion-invalid",
			Workspace = workspace,
			ExpectedCandidateSnapshotSha256 = "not-a-hash"
		}));
	CandidateTimelineSnapshot diverged = ContractSerializer
		.Deserialize<CandidateTimelineSnapshot>(ContractSerializer.Serialize(promoted));
	diverged.Tracks[0].Muted = !diverged.Tracks[0].Muted;
	Expect<InvalidOperationException>(() =>
		CandidatePromotionContract.ValidateRollbackSnapshot(valid, diverged));
	CandidateTimelineSnapshot partiallyPromoted = ContractSerializer
		.Deserialize<CandidateTimelineSnapshot>(ContractSerializer.Serialize(promoted));
	partiallyPromoted.Tracks.Single(track =>
		track.Name == mapping[0].FinalName).Name = mapping[0].CandidateName;
	CandidatePromotionContract.ValidateRecoverableTrackSet(
		valid,
		partiallyPromoted);
	CandidatePromotionContract.ValidateRestoredSnapshot(valid, snapshot);
});

Console.WriteLine("All iteration contract self-tests passed.");

static VegasJobEnvelope ValidEnvelope()
{
	DateTimeOffset now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
	JObject payload = JObject.Parse("""{"value":1}""");
	return new VegasJobEnvelope
	{
		SessionId = "session-1",
		JobId = "job-1",
		Sequence = 1,
		IdempotencyKey = "key-1",
		Operation = VegasOperations.CleanupCandidate,
		CreatedUtc = now,
		DeadlineUtc = now.AddMinutes(1),
		Payload = payload,
		PayloadSha256 = ContractHash.Compute(payload)
	};
}

static void Run(string name, Action test)
{
	test();
	Console.WriteLine($"PASS {name}");
}

static void Assert(bool condition)
{
	if (!condition) throw new InvalidOperationException("Assertion failed.");
}

static void Expect<T>(Action action) where T : Exception
{
	try { action(); }
	catch (T) { return; }
	throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
