using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AutoEditing.Iteration.Contracts.Assembly;

public sealed class AssemblyClipReference
{
	public string ReferenceId { get; set; } = "";
	public string MediaPath { get; set; } = "";
}

public sealed class AssemblySongEventReference
{
	public string EventId { get; set; } = "";
	public double EffectiveTimeSeconds { get; set; }
	public string RegionId { get; set; } = "";
	public string MusicalType { get; set; } = "";
	public int Priority { get; set; }
}

public sealed class AssemblySectionIntent
{
	public string SectionId { get; set; } = "";
	public string RegionId { get; set; } = "";
	public string EditorialRole { get; set; } = "";
	public string EnergyDirection { get; set; } = "";
	public string PacingIntent { get; set; } = "";
	public string Rationale { get; set; } = "";
}

public sealed class AssemblyClipIntent
{
	public int Order { get; set; }
	public AssemblyClipReference Clip { get; set; } = new();
	public string SectionId { get; set; } = "";
	public string EditorialRole { get; set; } = "";
	public string Rationale { get; set; } = "";
	public IList<string> AlternativeClipReferenceIds { get; set; } = new List<string>();
	public double Confidence { get; set; }
}

public sealed class AssemblySyncStrategy
{
	public string Density { get; set; } = "";
	public IList<string> PreferredMusicalTypes { get; set; } = new List<string>();
	public string Rationale { get; set; } = "";
}

public sealed class AssemblySketch
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string RequestId { get; set; } = "";
	public string EditorialThesis { get; set; } = "";
	public IList<AssemblySectionIntent> Sections { get; set; } = new List<AssemblySectionIntent>();
	public IList<AssemblyClipIntent> ClipOrder { get; set; } = new List<AssemblyClipIntent>();
	public AssemblySyncStrategy SyncStrategy { get; set; } = new();
	public IList<AssemblyReservation> Reservations { get; set; } =
		new List<AssemblyReservation>();
	public IList<AssemblyUncertainty> Uncertainties { get; set; } =
		new List<AssemblyUncertainty>();
}

public sealed class AssemblyReservation
{
	public string ReservationId { get; set; } = "";
	public string Purpose { get; set; } = "";
	public string RegionId { get; set; } = "";
	public IList<string> PreferredClipReferenceIds { get; set; } = new List<string>();
}

public sealed class AssemblyUncertainty
{
	public string UncertaintyId { get; set; } = "";
	public string Description { get; set; } = "";
	public string ResolutionSignal { get; set; } = "";
}

public sealed class VelocityCurvePoint
{
	public double OffsetSeconds { get; set; }
	public double Speed { get; set; } = 1;
}

public sealed class AssemblySourceWindow
{
	public double StartSeconds { get; set; }
	public double EndSeconds { get; set; }
	public double ConstantSpeed { get; set; } = 1;

	/// <summary>
	/// Optional fast/slow/fast retiming curve (2-7 points, offsets relative to
	/// StartSeconds, first at 0 and last at EndSeconds-StartSeconds). Null or
	/// empty means the window plays at ConstantSpeed throughout.
	/// </summary>
	public IList<VelocityCurvePoint>? VelocityCurve { get; set; }
}

public sealed class AssemblySyncDecision
{
	public string MusicEventId { get; set; } = "";
	public int KillIndex { get; set; }
}

public sealed class AssemblyProposalRejection
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public int Checkpoint { get; set; }
	public int ProposalRevision { get; set; }
	public int Attempt { get; set; }
	public int MaximumAttempts { get; set; }
	public string Diagnostic { get; set; } = "";
	public string ProposalRelativePath { get; set; } = "";
	public DateTimeOffset RejectedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssemblyDecisionAlternative
{
	public string Description { get; set; } = "";
	public string RejectedBecause { get; set; } = "";
}

public sealed class ClipStepDecision
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string RequestId { get; set; } = "";
	public int StepIndex { get; set; }
	public AssemblyClipReference Clip { get; set; } = new();
	public AssemblySourceWindow SourceWindow { get; set; } = new();
	public AssemblySyncDecision PrimarySync { get; set; } = new();
	public IList<AssemblySyncDecision> AdditionalSyncs { get; set; } =
		new List<AssemblySyncDecision>();
	public string Rationale { get; set; } = "";
	public IList<AssemblyDecisionAlternative> Alternatives { get; set; } =
		new List<AssemblyDecisionAlternative>();
	public double Confidence { get; set; }
}

/// <summary>
/// Truthful canonical summary of a human-accepted placement. Syncs may be empty
/// after live timeline reconciliation invalidates a proposed assignment.
/// </summary>
public sealed class AcceptedClipPlacementSummary
{
	public int StepIndex { get; set; }
	public AssemblyClipReference Clip { get; set; } = new();
	public double TimelineStartSeconds { get; set; }
	public double TimelineEndSeconds { get; set; }
	public double SourceStartSeconds { get; set; }
	public double SourceEndSeconds { get; set; }
	public double ConstantSpeed { get; set; } = 1;

	/// <summary>
	/// Present only when the accepted placement's velocity is not a single
	/// constant rate. Offsets are relative to SourceStartSeconds.
	/// </summary>
	public IList<VelocityCurvePoint>? VelocityCurve { get; set; }
	public IList<AssemblySyncDecision> SurvivingSyncs { get; set; } =
		new List<AssemblySyncDecision>();
	public bool WasHumanAdjusted { get; set; }
	public string AdjustmentRationale { get; set; } = "";
}

public sealed class ProgressiveAssemblyPlanningContext
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string RequestId { get; set; } = "";
	public int StepIndex { get; set; }
	public AssemblySketch Sketch { get; set; } = new();
	public IList<AcceptedClipPlacementSummary> AcceptedPrefix { get; set; } =
		new List<AcceptedClipPlacementSummary>();
	public IList<AssemblyClipReference> RemainingClips { get; set; } =
		new List<AssemblyClipReference>();
	public IList<AssemblySongEventReference> NearbySongContext { get; set; } =
		new List<AssemblySongEventReference>();
	public TimelineAdjustmentDelta TimelineAdjustment { get; set; }
	public string ScopedInstruction { get; set; } = "";
}

public static class AssemblyReferenceIds
{
	public static string ForClipPath(string mediaPath)
	{
		if (string.IsNullOrWhiteSpace(mediaPath))
			throw new ArgumentException("A media path is required.", nameof(mediaPath));
		string stable = mediaPath.Trim().Replace('\\', '/').ToLowerInvariant();
		using SHA256 sha = SHA256.Create();
		byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(stable));
		return "clip-" + string.Concat(hash.Take(12).Select(value => value.ToString("x2")));
	}
}

public static class ProgressiveAssemblyContractValidator
{
	public static void Validate(AssemblySketch sketch)
	{
		if (sketch == null) throw new ArgumentNullException(nameof(sketch));
		Schema(sketch.SchemaVersion, AssemblySketch.CurrentSchemaVersion, "assembly sketch");
		Text(sketch.RequestId, "Sketch request ID");
		Text(sketch.EditorialThesis, "Editorial thesis");
		if (sketch.Sections == null || sketch.Sections.Count == 0)
			throw new InvalidDataException("An assembly sketch requires at least one section.");
		HashSet<string> sections = new(StringComparer.Ordinal);
		foreach (AssemblySectionIntent section in sketch.Sections)
		{
			Text(section.SectionId, "Section ID");
			if (!sections.Add(section.SectionId))
				throw new InvalidDataException("Assembly section IDs must be unique.");
			Text(section.EditorialRole, "Section editorial role");
			Text(section.EnergyDirection, "Section energy direction");
			Text(section.PacingIntent, "Section pacing intent");
			Text(section.Rationale, "Section rationale");
		}
		if (sketch.ClipOrder == null || sketch.ClipOrder.Count == 0)
			throw new InvalidDataException("An assembly sketch requires an ordered clip list.");
		HashSet<string> clips = new(StringComparer.Ordinal);
		for (int index = 0; index < sketch.ClipOrder.Count; index++)
		{
			AssemblyClipIntent intent = sketch.ClipOrder[index];
			if (intent.Order != index + 1)
				throw new InvalidDataException("Assembly clip order must be contiguous and one-based.");
			Validate(intent.Clip);
			if (!clips.Add(intent.Clip.ReferenceId))
				throw new InvalidDataException("An assembly sketch cannot repeat a clip.");
			if (!sections.Contains(intent.SectionId))
				throw new InvalidDataException("A clip intent references an unknown section.");
			Text(intent.EditorialRole, "Clip editorial role");
			Text(intent.Rationale, "Clip rationale");
			if (intent.AlternativeClipReferenceIds == null)
				throw new InvalidDataException("Clip alternatives are required.");
			if (intent.Confidence < 0 || intent.Confidence > 1)
				throw new InvalidDataException("Clip-intent confidence must be between zero and one.");
		}
		foreach (AssemblyClipIntent intent in sketch.ClipOrder)
			if (intent.AlternativeClipReferenceIds.Any(id => !clips.Contains(id)))
				throw new InvalidDataException("A clip intent references an unknown alternative clip.");
		if (sketch.SyncStrategy == null)
			throw new InvalidDataException("An assembly sketch requires a sync strategy.");
		Text(sketch.SyncStrategy.Density, "Sync density");
		Text(sketch.SyncStrategy.Rationale, "Sync rationale");
		foreach (AssemblyReservation reservation in sketch.Reservations ??
			throw new InvalidDataException("Sketch reservations are required."))
		{
			Text(reservation.ReservationId, "Reservation ID");
			Text(reservation.Purpose, "Reservation purpose");
			if (reservation.PreferredClipReferenceIds == null)
				throw new InvalidDataException("Reservation clip preferences are required.");
			if (reservation.PreferredClipReferenceIds.Any(id => !clips.Contains(id)))
				throw new InvalidDataException("A reservation references an unknown clip.");
		}
		foreach (AssemblyUncertainty uncertainty in sketch.Uncertainties ??
			throw new InvalidDataException("Sketch uncertainties are required."))
		{
			Text(uncertainty.UncertaintyId, "Uncertainty ID");
			Text(uncertainty.Description, "Uncertainty description");
			Text(uncertainty.ResolutionSignal, "Uncertainty resolution signal");
		}
	}

	public static void Validate(ClipStepDecision decision)
	{
		if (decision == null) throw new ArgumentNullException(nameof(decision));
		Schema(decision.SchemaVersion, ClipStepDecision.CurrentSchemaVersion, "clip-step decision");
		Text(decision.RequestId, "Decision request ID");
		if (decision.StepIndex < 1) throw new InvalidDataException("Step index must be positive.");
		Validate(decision.Clip);
		if (decision.SourceWindow == null ||
			decision.SourceWindow.StartSeconds < 0 ||
			decision.SourceWindow.EndSeconds <= decision.SourceWindow.StartSeconds ||
			decision.SourceWindow.ConstantSpeed < 0.25 ||
			decision.SourceWindow.ConstantSpeed > 4)
			throw new InvalidDataException("The clip source window or constant speed is invalid.");
		ValidateVelocityCurve(
			decision.SourceWindow.EndSeconds - decision.SourceWindow.StartSeconds,
			decision.SourceWindow.VelocityCurve);
		Validate(decision.PrimarySync);
		HashSet<string> syncs = new(StringComparer.Ordinal)
		{
			decision.PrimarySync.MusicEventId
		};
		foreach (AssemblySyncDecision sync in decision.AdditionalSyncs ??
			throw new InvalidDataException("Additional sync decisions are required."))
		{
			Validate(sync);
			if (!syncs.Add(sync.MusicEventId))
				throw new InvalidDataException("A clip decision cannot reuse a musical event.");
		}
		Text(decision.Rationale, "Decision rationale");
		if (decision.Alternatives == null)
			throw new InvalidDataException("Decision alternatives are required.");
		foreach (AssemblyDecisionAlternative alternative in decision.Alternatives)
		{
			Text(alternative.Description, "Alternative description");
			Text(alternative.RejectedBecause, "Alternative rejection rationale");
		}
		if (decision.Confidence < 0 || decision.Confidence > 1)
			throw new InvalidDataException("Decision confidence must be between zero and one.");
	}

	public static void Validate(ProgressiveAssemblyPlanningContext context)
	{
		if (context == null) throw new ArgumentNullException(nameof(context));
		Schema(context.SchemaVersion, ProgressiveAssemblyPlanningContext.CurrentSchemaVersion,
			"progressive assembly context");
		Text(context.RequestId, "Context request ID");
		if (context.StepIndex < 1) throw new InvalidDataException("Context step index must be positive.");
		Validate(context.Sketch);
		if (!string.Equals(context.RequestId, context.Sketch.RequestId, StringComparison.Ordinal))
			throw new InvalidDataException("The sketch belongs to another planning request.");
		IList<AcceptedClipPlacementSummary> prefix = context.AcceptedPrefix ??
			throw new InvalidDataException("Accepted prefix is required.");
		for (int index = 0; index < prefix.Count; index++)
		{
			AcceptedClipPlacementSummary step = prefix[index];
			Validate(step);
			if (step.StepIndex != index + 1)
				throw new InvalidDataException(
					"Accepted placement summaries must be contiguous and one-based.");
		}
		foreach (AssemblyClipReference clip in context.RemainingClips ??
			throw new InvalidDataException("Remaining clips are required."))
			Validate(clip);
		foreach (AssemblySongEventReference songEvent in context.NearbySongContext ??
			throw new InvalidDataException("Nearby song context is required."))
		{
			Text(songEvent.EventId, "Song event ID");
			if (songEvent.EffectiveTimeSeconds < 0)
				throw new InvalidDataException("Song event time cannot be negative.");
		}
	}

	public static void Validate(AcceptedClipPlacementSummary summary)
	{
		if (summary == null) throw new ArgumentNullException(nameof(summary));
		if (summary.StepIndex < 1) throw new InvalidDataException("Accepted step index must be positive.");
		Validate(summary.Clip);
		if (summary.TimelineStartSeconds < 0 ||
			summary.TimelineEndSeconds <= summary.TimelineStartSeconds ||
			summary.SourceStartSeconds < 0 ||
			summary.SourceEndSeconds <= summary.SourceStartSeconds ||
			summary.ConstantSpeed < 0.25 || summary.ConstantSpeed > 4)
			throw new InvalidDataException("Accepted placement timing is invalid.");
		ValidateVelocityCurve(
			summary.SourceEndSeconds - summary.SourceStartSeconds,
			summary.VelocityCurve);
		foreach (AssemblySyncDecision sync in summary.SurvivingSyncs ??
			throw new InvalidDataException("Accepted surviving syncs are required."))
			Validate(sync);
		if (summary.WasHumanAdjusted && string.IsNullOrWhiteSpace(summary.AdjustmentRationale))
			throw new InvalidDataException(
				"A human-adjusted accepted placement requires an adjustment rationale.");
	}

	private static void Validate(AssemblyClipReference clip)
	{
		if (clip == null) throw new InvalidDataException("A clip reference is required.");
		Text(clip.ReferenceId, "Clip reference ID");
		Text(clip.MediaPath, "Clip media path");
		if (!string.Equals(clip.ReferenceId, AssemblyReferenceIds.ForClipPath(clip.MediaPath),
			StringComparison.Ordinal))
			throw new InvalidDataException("A clip reference ID does not match its media path.");
	}

	private static void Validate(AssemblySyncDecision sync)
	{
		if (sync == null) throw new InvalidDataException("A sync decision is required.");
		Text(sync.MusicEventId, "Music event ID");
		if (sync.KillIndex < 0) throw new InvalidDataException("Kill index cannot be negative.");
	}

	/// <summary>
	/// A velocity curve is a fast/slow/fast retiming shape: 2-7 points, offsets
	/// relative to the window start, strictly increasing, spanning exactly the
	/// window's full duration, each a supported constant rate.
	/// </summary>
	private static void ValidateVelocityCurve(
		double windowDurationSeconds,
		IList<VelocityCurvePoint>? curve)
	{
		if (curve == null || curve.Count == 0) return;
		if (curve.Count < 2 || curve.Count > 7)
			throw new InvalidDataException(
				"A velocity curve must have between 2 and 7 points.");
		if (Math.Abs(curve[0].OffsetSeconds) > 0.000001)
			throw new InvalidDataException(
				"A velocity curve must start at the window's first frame.");
		if (Math.Abs(curve[curve.Count - 1].OffsetSeconds - windowDurationSeconds) > 0.000001)
			throw new InvalidDataException(
				"A velocity curve must end at the window's last frame.");
		for (int index = 0; index < curve.Count; index++)
		{
			VelocityCurvePoint point = curve[index];
			if (point.Speed < 0.25 || point.Speed > 4)
				throw new InvalidDataException(
					"A velocity curve point rate is unsupported.");
			if (index > 0 && point.OffsetSeconds <= curve[index - 1].OffsetSeconds + 0.000001)
				throw new InvalidDataException(
					"A velocity curve's offsets must strictly increase.");
		}
	}

	private static void Text(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException(name + " is required.");
	}

	private static void Schema(int actual, int expected, string name)
	{
		if (actual != expected) throw new InvalidDataException(
			$"Unsupported {name} schema version {actual}.");
	}
}
