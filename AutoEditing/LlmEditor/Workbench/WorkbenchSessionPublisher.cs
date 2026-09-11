using AutoEditing.Iteration.Contracts;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.LlmEditor.Iteration;
using AutoEditing.LlmEditor.Sessions;
using Core.Domain.Planning;
using Newtonsoft.Json.Linq;

namespace AutoEditing.LlmEditor.Workbench;

/// <summary>
/// Publishes the durable, UI-neutral projection consumed by an LLM workbench.
/// The workbench may be restarted without owning the planner or VEGAS process.
/// </summary>
internal sealed class WorkbenchSessionPublisher : IEditIterationObserver
{
	private readonly SessionPathResolver paths;
	private readonly AtomicFileWriter writer;
	private readonly SessionEventWriter events;
	private readonly Func<DateTimeOffset> clock;
	private EditSessionManifest manifest;

	private WorkbenchSessionPublisher(
		SessionPathResolver paths,
		AtomicFileWriter writer,
		SessionEventWriter events,
		EditSessionManifest manifest,
		Func<DateTimeOffset> clock)
	{
		this.paths = paths;
		this.writer = writer;
		this.events = events;
		this.manifest = manifest;
		this.clock = clock;
	}

	public string SessionRoot => paths.Root;

	public EditSessionState State => manifest.State;

	public void PublishTimeline(int iteration, CandidateTimelineSnapshot snapshot)
	{
		if (iteration < 1) throw new ArgumentOutOfRangeException(nameof(iteration));
		ArgumentNullException.ThrowIfNull(snapshot);
		writer.WriteText(
			paths.Resolve($"iterations/{iteration:D4}/timeline.json"),
			ContractSerializer.Serialize(snapshot));
	}

	public void PublishPreview(int iteration, RenderCandidatePreviewResult preview)
	{
		if (iteration < 1) throw new ArgumentOutOfRangeException(nameof(iteration));
		ArgumentNullException.ThrowIfNull(preview);
		writer.WriteText(
			paths.Resolve($"iterations/{iteration:D4}/preview.json"),
			ContractSerializer.Serialize(preview));
	}

	public void PublishProgress(EditSessionProgress progress)
	{
		if (progress == null) throw new ArgumentNullException(nameof(progress));
		if (!string.Equals(progress.SessionId, manifest.SessionId, StringComparison.Ordinal))
			throw new InvalidDataException("Progress session ID does not match the workbench session.");
		writer.WriteText(paths.Resolve("progress.json"), ContractSerializer.Serialize(progress));
	}

	public static WorkbenchSessionPublisher Create(
		string sessionsRoot,
		string sessionId,
		Func<DateTimeOffset>? clock = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionsRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		EditSessionIdValidator.Validate(sessionId);

		SessionPathResolver paths = new SessionPathResolver(
			Path.Combine(Path.GetFullPath(sessionsRoot), sessionId));
		AtomicFileWriter writer = new AtomicFileWriter();
		DateTimeOffset now = (clock ?? (() => DateTimeOffset.UtcNow))();
		EditSessionManifest manifest = new EditSessionManifest
		{
			SessionId = sessionId,
			Revision = 1,
			State = EditSessionState.Created,
			CreatedUtc = now,
			UpdatedUtc = now
		};
		string manifestPath = paths.Resolve("manifest.json");
		if (File.Exists(manifestPath))
			throw new IOException("Workbench session already exists: " + sessionId);
		writer.WriteText(manifestPath, ContractSerializer.Serialize(manifest));
		SessionEventWriter events = new SessionEventWriter(paths.Resolve("events.ndjson"));
		events.Append("session-created", new { sessionId }, now);
		return new WorkbenchSessionPublisher(paths, writer, events, manifest, clock ?? (() => DateTimeOffset.UtcNow));
	}

	public static WorkbenchSessionPublisher Open(
		string sessionsRoot,
		string sessionId,
		Func<DateTimeOffset>? clock = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionsRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		EditSessionIdValidator.Validate(sessionId);
		SessionPathResolver paths = new SessionPathResolver(
			Path.Combine(Path.GetFullPath(sessionsRoot), sessionId));
		string manifestPath = paths.Resolve("manifest.json");
		if (!File.Exists(manifestPath))
			throw new FileNotFoundException("Workbench session does not exist.", manifestPath);
		EditSessionManifest manifest =
			ContractSerializer.Deserialize<EditSessionManifest>(File.ReadAllText(manifestPath));
		if (!string.Equals(manifest.SessionId, sessionId, StringComparison.Ordinal))
			throw new InvalidDataException("Workbench manifest session ID does not match its directory.");
		return new WorkbenchSessionPublisher(
			paths,
			new AtomicFileWriter(),
			new SessionEventWriter(paths.Resolve("events.ndjson")),
			manifest,
			clock ?? (() => DateTimeOffset.UtcNow));
	}

	public void TransitionTo(EditSessionState state, string reason)
	{
		EditSessionStateTransitionValidator.Validate(manifest.State, state);
		manifest.State = state;
		manifest.Revision++;
		manifest.UpdatedUtc = clock();
		WriteManifest();
		events.Append(
			"state-changed",
			new { state = state.ToString(), reason = reason ?? "" },
			manifest.UpdatedUtc);
	}

	public Task OnSnapshotAsync(
		EditIterationSnapshot snapshot,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		cancellationToken.ThrowIfCancellationRequested();
		if (snapshot.Iteration < 1)
			throw new InvalidDataException("A workbench iteration number must be positive.");

		string directory = $"iterations/{snapshot.Iteration:D4}";
		string candidateRelativePath = directory + "/candidate.json";
		string traceRelativePath = directory + "/trace.json";
		string candidateJson = EditPlanDocumentSerializer.SerializePlan(snapshot.Candidate);
		writer.WriteText(paths.Resolve(candidateRelativePath), candidateJson);
		string candidateHash = ContractHash.Compute(JToken.Parse(candidateJson));
		AutoEditing.Iteration.Contracts.Iterations.EditIterationSnapshot persistedSnapshot =
			new AutoEditing.Iteration.Contracts.Iterations.EditIterationSnapshot
			{
				SessionId = manifest.SessionId,
				Iteration = snapshot.Iteration,
				CreatedUtc = clock(),
				Candidate = snapshot.Candidate,
				CandidateHash = candidateHash,
				Decisions = snapshot.Decisions.Select(decision =>
					new AutoEditing.Iteration.Contracts.Iterations.EditDecisionRecord
					{
						DecisionId = decision.DecisionId,
						Summary = decision.Summary,
						Confidence = decision.Confidence ?? 0,
						EvidenceIds = decision.EvidenceIds.ToArray()
					}).ToArray(),
				Findings = snapshot.Findings.ToArray(),
				Evidence = snapshot.Evidence.ToArray()
			};
		writer.WriteText(
			paths.Resolve(directory + "/snapshot.json"),
			ContractSerializer.Serialize(persistedSnapshot));
		writer.WriteText(paths.Resolve(traceRelativePath), ContractSerializer.Serialize(new
		{
			schemaVersion = ContractSchema.CurrentVersion,
			sessionId = manifest.SessionId,
			iteration = snapshot.Iteration,
			createdUtc = clock(),
			snapshot.Phase,
			snapshot.Summary,
			snapshot.Decisions,
			candidatePath = candidateRelativePath,
			candidateSha256 = new SessionArtifactHasher().ComputeSha256(paths.Resolve(candidateRelativePath)),
			candidateHash
		}));

		manifest.CurrentIteration = snapshot.Iteration;
		manifest.Revision++;
		manifest.UpdatedUtc = clock();
		manifest.Metadata["lastPhase"] = snapshot.Phase;
		manifest.Metadata["latestTrace"] = traceRelativePath;
		WriteManifest();
		events.Append(
			"iteration-published",
			new { iteration = snapshot.Iteration, phase = snapshot.Phase, tracePath = traceRelativePath },
			manifest.UpdatedUtc);
		return Task.CompletedTask;
	}

	private void WriteManifest() =>
		writer.WriteText(paths.Resolve("manifest.json"), ContractSerializer.Serialize(manifest));

}
