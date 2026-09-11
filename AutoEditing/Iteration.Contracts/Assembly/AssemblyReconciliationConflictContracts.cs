using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoEditing.Iteration.Contracts.Assembly;

public enum AssemblyReconciliationConflictKind
{
	MissingExpectedEvent,
	UnexpectedEvent,
	AmbiguousIdentity,
	MediaReplacement,
	UnsupportedTimelineEdit
}

public enum AssemblyReconciliationResolutionKind
{
	RestoreExactProposal,
	ExcludeCurrentClip,
	AdoptKnownRemainingEvent,
	DeferAndPause
}

public sealed class AssemblyReconciliationConflictIssue
{
	public AssemblyReconciliationConflictKind Kind { get; set; }
	public string ExpectedPlacementId { get; set; } = "";
	public string ExpectedMediaPath { get; set; } = "";
	public string Detail { get; set; } = "";
}

public sealed class AssemblyReconciliationEventCandidate
{
	public string CandidateId { get; set; } = "";
	public string PlacementId { get; set; } = "";
	public string MediaPath { get; set; } = "";
	public double TimelineStartSeconds { get; set; }
	public double TimelineDurationSeconds { get; set; }
	public double SourceOffsetSeconds { get; set; }
	public double ConstantSpeed { get; set; } = 1;
	public bool IsKnownSelectedClip { get; set; }
	public bool IsRemainingClip { get; set; }
	public bool CanAdoptAsCurrent { get; set; }
	public bool CanAdoptAsAdditional { get; set; }
	public string AdoptionConstraint { get; set; } = "";
}

public sealed class AssemblyReconciliationConflict
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string ConflictId { get; set; } = "";
	public string SessionId { get; set; } = "";
	public int Checkpoint { get; set; }
	public string CurrentClipPath { get; set; } = "";
	public string FailureSummary { get; set; } = "";
	public IList<AssemblyReconciliationConflictIssue> Issues { get; set; } =
		new List<AssemblyReconciliationConflictIssue>();
	public IList<AssemblyReconciliationEventCandidate> Candidates { get; set; } =
		new List<AssemblyReconciliationEventCandidate>();
	public IList<AssemblyReconciliationResolutionKind> SupportedResolutions { get; set; } =
		new List<AssemblyReconciliationResolutionKind>();
	public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class AssemblyReconciliationResolution
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string ResolutionId { get; set; } = "";
	public string ConflictId { get; set; } = "";
	public string SessionId { get; set; } = "";
	public int Checkpoint { get; set; }
	public AssemblyReconciliationResolutionKind Kind { get; set; }
	public string TargetCandidateId { get; set; } = "";
	public string Instruction { get; set; } = "";
	public string ResolvedBy { get; set; } = "";
	public DateTimeOffset ResolvedUtc { get; set; }
}

public static class AssemblyReconciliationConflictValidator
{
	public static void Validate(AssemblyReconciliationConflict value)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Schema(value.SchemaVersion, AssemblyReconciliationConflict.CurrentSchemaVersion);
		Text(value.ConflictId, "Conflict ID");
		Text(value.SessionId, "Conflict session ID");
		if (value.Checkpoint < 1)
			throw new InvalidDataException("Conflict checkpoint must be positive.");
		Text(value.CurrentClipPath, "Current conflict clip path");
		Text(value.FailureSummary, "Conflict failure summary");
		if (value.Issues == null || value.Issues.Count == 0)
			throw new InvalidDataException("A reconciliation conflict requires an issue.");
		foreach (AssemblyReconciliationConflictIssue issue in value.Issues)
		{
			if (!Enum.IsDefined(typeof(AssemblyReconciliationConflictKind), issue.Kind))
				throw new InvalidDataException("A reconciliation conflict kind is invalid.");
			Text(issue.Detail, "Conflict issue detail");
		}
		HashSet<string> candidateIds = new(StringComparer.Ordinal);
		foreach (AssemblyReconciliationEventCandidate candidate in value.Candidates ??
			throw new InvalidDataException("Conflict candidates are required."))
		{
			Text(candidate.CandidateId, "Conflict candidate ID");
			if (!candidateIds.Add(candidate.CandidateId))
				throw new InvalidDataException("Conflict candidate IDs must be unique.");
			Text(candidate.MediaPath, "Conflict candidate media path");
			if (candidate.TimelineStartSeconds < 0 ||
				candidate.TimelineDurationSeconds <= 0 ||
				candidate.SourceOffsetSeconds < 0)
				throw new InvalidDataException("Conflict candidate timing is invalid.");
			if (candidate.ConstantSpeed < 0.25 || candidate.ConstantSpeed > 4)
				throw new InvalidDataException("Conflict candidate speed is invalid.");
			if ((candidate.CanAdoptAsCurrent || candidate.CanAdoptAsAdditional) &&
				(!candidate.IsKnownSelectedClip || !candidate.IsRemainingClip))
				throw new InvalidDataException(
					"Only a known remaining clip may be offered for adoption.");
		}
		if (value.SupportedResolutions == null ||
			!value.SupportedResolutions.Contains(
				AssemblyReconciliationResolutionKind.RestoreExactProposal) ||
			!value.SupportedResolutions.Contains(
				AssemblyReconciliationResolutionKind.DeferAndPause))
			throw new InvalidDataException(
				"Every conflict must support exact restoration and deferral.");
		if (value.SupportedResolutions.Distinct().Count() !=
			value.SupportedResolutions.Count)
			throw new InvalidDataException("Conflict resolution choices must be unique.");
		if (value.CreatedUtc == default)
			throw new InvalidDataException("Conflict creation time is required.");
	}

	public static void Validate(
		AssemblyReconciliationResolution value,
		AssemblyReconciliationConflict conflict)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Validate(conflict);
		Schema(value.SchemaVersion, AssemblyReconciliationResolution.CurrentSchemaVersion);
		Text(value.ResolutionId, "Resolution ID");
		if (!string.Equals(value.ConflictId, conflict.ConflictId, StringComparison.Ordinal) ||
			!string.Equals(value.SessionId, conflict.SessionId, StringComparison.Ordinal) ||
			value.Checkpoint != conflict.Checkpoint)
			throw new InvalidDataException(
				"The reconciliation resolution targets another conflict.");
		if (!conflict.SupportedResolutions.Contains(value.Kind))
			throw new InvalidDataException(
				"The selected reconciliation resolution is not supported.");
		if (value.Kind == AssemblyReconciliationResolutionKind.AdoptKnownRemainingEvent)
		{
			Text(value.TargetCandidateId, "Adoption candidate ID");
			AssemblyReconciliationEventCandidate candidate = conflict.Candidates
				.SingleOrDefault(item => string.Equals(
					item.CandidateId, value.TargetCandidateId, StringComparison.Ordinal))
				?? throw new InvalidDataException(
					"The adoption candidate is not part of this conflict.");
			if (!candidate.CanAdoptAsCurrent && !candidate.CanAdoptAsAdditional)
				throw new InvalidDataException(
					"The selected event cannot be deterministically adopted.");
		}
		else if (!string.IsNullOrWhiteSpace(value.TargetCandidateId))
		{
			throw new InvalidDataException(
				"Only an adoption resolution may target a candidate event.");
		}
		Text(value.ResolvedBy, "Resolution actor");
		if (value.ResolvedUtc == default)
			throw new InvalidDataException("Resolution time is required.");
	}

	private static void Text(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidDataException(name + " is required.");
	}

	private static void Schema(int actual, int expected)
	{
		if (actual != expected)
			throw new InvalidDataException(
				"Unsupported reconciliation conflict schema version " + actual + ".");
	}
}
