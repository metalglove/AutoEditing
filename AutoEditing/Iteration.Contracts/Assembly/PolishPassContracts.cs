using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;

namespace AutoEditing.Iteration.Contracts.Assembly;

public enum PolishPassKind { Effects, Audio }
public enum PolishPlanStatus
{
	AwaitingApproval, Approved, Rejected, Materializing, Applied,
	PreviewReady, Accepted, Failed
}
public enum PolishApprovalDisposition { Approve, Reject }
public enum PolishActionOutcome { Applied, Unsupported, Rejected, Failed }

/// <summary>Exact renderer capabilities used to create a polish plan.</summary>
public sealed class PolishRendererCapabilities
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public bool ScreenPump { get; set; } = true;
	public bool SongTrack { get; set; } = true;
	public bool ReviewedGunHitSfx { get; set; } = true;
	public IList<string> UnsupportedVisualTreatments { get; set; } =
		new List<string>
		{
			EditorialUse.Flash.ToString(), EditorialUse.Shake.ToString(),
			EditorialUse.CutOrTransition.ToString(),
			EditorialUse.CinematicTransition.ToString(),
			EditorialUse.TitleReveal.ToString(),
			EditorialUse.SpeedChange.ToString()
		};
}

public sealed class EffectsPassAction
{
	public string ActionId { get; set; } = "";
	public string PlacementPath { get; set; } = "";
	public string MusicEventId { get; set; } = "";
	public double TimelineTimeSeconds { get; set; }
	public double LocalTimeSeconds { get; set; }
	public double Intensity { get; set; }
	public double DurationSeconds { get; set; }
	public string RecipeId { get; set; } = "";
	public string Reason { get; set; } = "";
}

public sealed class EffectsPassPlan
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string PlanId { get; set; } = "";
	public int Revision { get; set; }
	public string BaseRoughCutSha256 { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
	public PolishRendererCapabilities Capabilities { get; set; } = new();
	public IList<EffectsPassAction> Actions { get; set; } = new List<EffectsPassAction>();
	public IList<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class AudioPassSongAction
{
	public string ActionId { get; set; } = "song";
	public string SongPath { get; set; } = "";
	public double TimelineStartSeconds { get; set; }
	public double TrackGain { get; set; } = 0.5;
}

public sealed class AudioPassSfxAction
{
	public string ActionId { get; set; } = "";
	public string PlacementPath { get; set; } = "";
	public int ConfirmedKillIndex { get; set; }
	public double ConfirmationTimeSeconds { get; set; }
	public string Gun { get; set; } = "";
	public ShotOutcome Outcome { get; set; }
	public string PreferredTemplateId { get; set; } = "";
	public double TrackGain { get; set; } = 0.6;
}

public sealed class AudioPassPlan
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string PlanId { get; set; } = "";
	public int Revision { get; set; }
	public string BaseRoughCutSha256 { get; set; } = "";
	public string EffectsPassSha256 { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
	public PolishRendererCapabilities Capabilities { get; set; } = new();
	public AudioPassSongAction Song { get; set; }
	public IList<AudioPassSfxAction> Sfx { get; set; } = new List<AudioPassSfxAction>();
	public IList<string> Diagnostics { get; set; } = new List<string>();
}

public sealed class PolishPassApproval
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public PolishPassKind Pass { get; set; }
	public string PlanId { get; set; } = "";
	public int PlanRevision { get; set; }
	public string PlanSha256 { get; set; } = "";
	public PolishApprovalDisposition Disposition { get; set; }
	public string Note { get; set; } = "";
	public string DecidedBy { get; set; } = "";
	public DateTimeOffset DecidedUtc { get; set; }
}

public sealed class PolishActionResult
{
	public string ActionId { get; set; } = "";
	public PolishActionOutcome Outcome { get; set; }
	public string Detail { get; set; } = "";
}

public sealed class PolishPassMaterialization
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public PolishPassKind Pass { get; set; }
	public string PlanId { get; set; } = "";
	public int PlanRevision { get; set; }
	public string PlanSha256 { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; }
	public DateTimeOffset CompletedUtc { get; set; }
	public bool FullyApplied { get; set; }
	public IList<PolishActionResult> Actions { get; set; } = new List<PolishActionResult>();
	public CandidateTimelineSnapshot Snapshot { get; set; }
}

public sealed class PolishPassPreviewManifest
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public PolishPassKind Pass { get; set; }
	public string PlanId { get; set; } = "";
	public int PlanRevision { get; set; }
	public string PlanSha256 { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; }
	public DateTimeOffset CompletedUtc { get; set; }
	public string RenderProfileId { get; set; } = "";
	public TimeSpan TimelineStart { get; set; }
	public TimeSpan TimelineEnd { get; set; }
	public IList<RoughCutRenderChunk> Chunks { get; set; } = new List<RoughCutRenderChunk>();
}

public sealed class AcceptedPolishPass
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public PolishPassKind Pass { get; set; }
	public string PlanSha256 { get; set; } = "";
	public string MaterializationSha256 { get; set; } = "";
	public string PreviewSha256 { get; set; } = "";
	public string AcceptedBy { get; set; } = "";
	public DateTimeOffset AcceptedUtc { get; set; }
}

/// <summary>
/// Terminal rejection of a rendered pass revision. The coordinator must
/// restore the accepted candidate baseline before another revision is applied.
/// This artifact deliberately does not claim that timeline rollback occurred.
/// </summary>
public sealed class RejectedPolishPassPreview
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public PolishPassKind Pass { get; set; }
	public string PlanSha256 { get; set; } = "";
	public string MaterializationSha256 { get; set; } = "";
	public string PreviewSha256 { get; set; } = "";
	public string RejectedBy { get; set; } = "";
	public string Note { get; set; } = "";
	public bool BaselineRestoreRequired { get; set; } = true;
	public DateTimeOffset RejectedUtc { get; set; }
}

/// <summary>
/// Durable proof that the coordinator restored the accepted baseline after a
/// rendered polish revision was rejected. The intent is written before any
/// VEGAS mutation and the completion record only after the restored workspace
/// has been read back and validated.
/// </summary>
public sealed class PolishBaselineRestoration
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public PolishPassKind RejectedPass { get; set; }
	public string RejectedPlanSha256 { get; set; } = "";
	public string BaselineRoughCutSha256 { get; set; } = "";
	public string AcceptedEffectsSha256 { get; set; } = "";
	public string AttemptId { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; } = new();
	public bool Completed { get; set; }
	public string RestoredSnapshotSha256 { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
	public DateTimeOffset? CompletedUtc { get; set; }
}

public sealed class PolishPassStateRecord
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public PolishPassKind Pass { get; set; }
	public string PlanId { get; set; } = "";
	public int PlanRevision { get; set; }
	public string PlanSha256 { get; set; } = "";
	public long Sequence { get; set; }
	public PolishPlanStatus Status { get; set; }
	public string Reason { get; set; } = "";
	public DateTimeOffset RecordedUtc { get; set; }
}

public static class PolishPassContractValidator
{
	private const double Tolerance = 0.001;

	public static void Validate(EffectsPassPlan plan)
	{
		if (plan == null) throw new ArgumentNullException(nameof(plan));
		Schema(plan.SchemaVersion, EffectsPassPlan.CurrentSchemaVersion, "effects plan");
		Header(plan.SessionId, plan.PlanId, plan.Revision, plan.BaseRoughCutSha256, plan.CreatedUtc);
		Capabilities(plan.Capabilities);
		HashSet<string> ids = new(StringComparer.Ordinal);
		HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);
		IList<EffectsPassAction> actions = plan.Actions ??
			throw new InvalidDataException("Effects actions are required.");
		foreach (EffectsPassAction action in actions)
		{
			Text(action.ActionId, "Effect action ID");
			if (!ids.Add(action.ActionId))
				throw new InvalidDataException("Effect action IDs must be unique.");
			Absolute(action.PlacementPath, "Effect placement path");
			string target = Path.GetFullPath(action.PlacementPath) + "|" +
				Math.Round(action.LocalTimeSeconds * 1000).ToString(
					System.Globalization.CultureInfo.InvariantCulture);
			if (!targets.Add(target))
				throw new InvalidDataException(
					"Effects actions may not duplicate one placement-time target.");
			if (!Finite(action.TimelineTimeSeconds) || !Finite(action.LocalTimeSeconds) ||
				!Finite(action.Intensity) || !Finite(action.DurationSeconds) ||
				action.TimelineTimeSeconds < 0 || action.LocalTimeSeconds < 0 ||
				action.Intensity < 0 || action.Intensity > 1 || action.DurationSeconds <= 0)
				throw new InvalidDataException("An effect action has invalid timing or intensity.");
			Text(action.RecipeId, "Effect recipe");
			Text(action.Reason, "Effect reason");
		}
		Diagnostics(plan.Diagnostics);
		if (!plan.Capabilities.ScreenPump && actions.Count != 0)
			throw new InvalidDataException("Effects actions require screen-pump capability.");
	}

	public static void Validate(AudioPassPlan plan)
	{
		if (plan == null) throw new ArgumentNullException(nameof(plan));
		Schema(plan.SchemaVersion, AudioPassPlan.CurrentSchemaVersion, "audio plan");
		Header(plan.SessionId, plan.PlanId, plan.Revision, plan.BaseRoughCutSha256, plan.CreatedUtc);
		Hash(plan.EffectsPassSha256, "Accepted effects-pass hash");
		Capabilities(plan.Capabilities);
		HashSet<string> ids = new(StringComparer.Ordinal);
		if (plan.Song != null)
		{
			if (!plan.Capabilities.SongTrack)
				throw new InvalidDataException("The plan includes a song without song capability.");
			Text(plan.Song.ActionId, "Song action ID");
			ids.Add(plan.Song.ActionId);
			Absolute(plan.Song.SongPath, "Song path");
			if (!Finite(plan.Song.TimelineStartSeconds) ||
				!Finite(plan.Song.TrackGain) ||
				Math.Abs(plan.Song.TimelineStartSeconds) > Tolerance ||
				Math.Abs(plan.Song.TrackGain - 0.5) > Tolerance)
				throw new InvalidDataException("The supported song treatment starts at zero at gain 0.5.");
		}
		IList<AudioPassSfxAction> sfx = plan.Sfx ??
			throw new InvalidDataException("SFX actions are required.");
		if (!plan.Capabilities.ReviewedGunHitSfx && sfx.Count != 0)
			throw new InvalidDataException("The plan includes SFX without reviewed-hit capability.");
		foreach (AudioPassSfxAction action in sfx)
		{
			Text(action.ActionId, "SFX action ID");
			if (!ids.Add(action.ActionId))
				throw new InvalidDataException("Audio action IDs must be unique.");
			Absolute(action.PlacementPath, "SFX placement path");
			if (!Finite(action.ConfirmationTimeSeconds) || !Finite(action.TrackGain) ||
				action.ConfirmedKillIndex < 0 || action.ConfirmationTimeSeconds < 0 ||
				Math.Abs(action.TrackGain - 0.6) > Tolerance)
				throw new InvalidDataException("An SFX action has invalid timing or gain.");
			Text(action.Gun, "SFX gun");
		}
		Diagnostics(plan.Diagnostics);
	}

	public static void Validate(PolishPassApproval value, EffectsPassPlan plan, string hash) =>
		Approval(value, PolishPassKind.Effects, plan.SessionId, plan.PlanId, plan.Revision, hash);
	public static void Validate(PolishPassApproval value, AudioPassPlan plan, string hash) =>
		Approval(value, PolishPassKind.Audio, plan.SessionId, plan.PlanId, plan.Revision, hash);

	public static void Validate(PolishPassMaterialization value)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Schema(value.SchemaVersion, PolishPassMaterialization.CurrentSchemaVersion, "materialization");
		Text(value.SessionId, "Materialization session ID");
		Text(value.PlanId, "Materialization plan ID");
		if (value.PlanRevision < 1) throw new InvalidDataException("Materialization revision is invalid.");
		Hash(value.PlanSha256, "Materialization plan hash");
		value.Workspace?.Validate();
		if (value.Workspace == null || value.CompletedUtc == default || value.Snapshot == null)
			throw new InvalidDataException("Materialization workspace, time, and snapshot are required.");
		if (!string.Equals(value.Workspace.SessionId, value.SessionId,
			StringComparison.Ordinal))
			throw new InvalidDataException(
				"Materialization workspace belongs to another session.");
		value.Snapshot.Workspace?.Validate();
		if (value.Snapshot.Workspace == null ||
			!string.Equals(value.Snapshot.Workspace.ToString(),
				value.Workspace.ToString(), StringComparison.Ordinal))
			throw new InvalidDataException(
				"Materialization snapshot targets another workspace.");
		if (value.Actions == null)
			throw new InvalidDataException("Materialization results are required.");
		if (value.FullyApplied != value.Actions.All(x => x.Outcome == PolishActionOutcome.Applied))
			throw new InvalidDataException("FullyApplied must reflect every action result.");
		foreach (PolishActionResult action in value.Actions)
		{
			Text(action.ActionId, "Materialization action ID");
			Text(action.Detail, "Materialization action detail");
		}
	}

	public static void Validate(PolishPassPreviewManifest value)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Schema(value.SchemaVersion, PolishPassPreviewManifest.CurrentSchemaVersion, "preview");
		Text(value.SessionId, "Preview session ID");
		Text(value.PlanId, "Preview plan ID");
		if (value.PlanRevision < 1) throw new InvalidDataException("Preview revision is invalid.");
		Hash(value.PlanSha256, "Preview plan hash");
		value.Workspace?.Validate();
		if (value.Workspace == null || value.CompletedUtc == default ||
			value.TimelineStart < TimeSpan.Zero ||
			value.TimelineEnd <= value.TimelineStart ||
			value.Chunks == null || value.Chunks.Count == 0)
			throw new InvalidDataException("Preview workspace, time, and chunks are required.");
		Text(value.RenderProfileId, "Preview render profile");
		TimeSpan expected = value.TimelineStart;
		for (int i = 0; i < value.Chunks.Count; i++)
		{
			RoughCutRenderChunk chunk = value.Chunks[i];
			if (chunk.ChunkIndex != i + 1 || chunk.Duration <= TimeSpan.Zero ||
				chunk.Duration > TimeSpan.FromSeconds(20) ||
				(chunk.Start - expected).Duration() > TimeSpan.FromMilliseconds(2))
				throw new InvalidDataException("Preview chunks are invalid.");
			Relative(chunk.OutputRelativePath, "Preview output path");
			Hash(chunk.Sha256, "Preview chunk hash");
			expected = chunk.Start + chunk.Duration;
		}
		if ((expected - value.TimelineEnd).Duration() > TimeSpan.FromMilliseconds(2))
			throw new InvalidDataException("Preview chunks do not cover the whole timeline.");
	}

	public static void Validate(AcceptedPolishPass value)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Schema(value.SchemaVersion, AcceptedPolishPass.CurrentSchemaVersion, "accepted pass");
		Text(value.SessionId, "Accepted session ID");
		Hash(value.PlanSha256, "Accepted plan hash");
		Hash(value.MaterializationSha256, "Accepted materialization hash");
		Hash(value.PreviewSha256, "Accepted preview hash");
		Text(value.AcceptedBy, "Accepted actor");
		if (value.AcceptedUtc == default) throw new InvalidDataException("Accepted time is required.");
	}

	public static void Validate(RejectedPolishPassPreview value)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Schema(value.SchemaVersion, RejectedPolishPassPreview.CurrentSchemaVersion,
			"rejected preview");
		Text(value.SessionId, "Rejected preview session ID");
		Hash(value.PlanSha256, "Rejected preview plan hash");
		Hash(value.MaterializationSha256, "Rejected materialization hash");
		Hash(value.PreviewSha256, "Rejected preview hash");
		Text(value.RejectedBy, "Rejected preview actor");
		if (!value.BaselineRestoreRequired)
			throw new InvalidDataException(
				"A rejected materialized pass must require explicit baseline restoration.");
		if (value.RejectedUtc == default)
			throw new InvalidDataException("Rejected preview time is required.");
	}

	public static void Validate(PolishBaselineRestoration value)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Schema(
			value.SchemaVersion,
			PolishBaselineRestoration.CurrentSchemaVersion,
			"baseline restoration");
		Text(value.SessionId, "Restoration session ID");
		Hash(value.RejectedPlanSha256, "Rejected plan hash");
		Hash(value.BaselineRoughCutSha256, "Baseline rough-cut hash");
		if (value.RejectedPass == PolishPassKind.Audio)
			Hash(value.AcceptedEffectsSha256, "Accepted effects hash");
		else if (!string.IsNullOrEmpty(value.AcceptedEffectsSha256))
			throw new InvalidDataException(
				"Effects baseline restoration must not claim an accepted effects hash.");
		Text(value.AttemptId, "Restoration attempt ID");
		value.Workspace?.Validate();
		if (value.Workspace == null ||
			!string.Equals(
				value.Workspace.SessionId,
				value.SessionId,
				StringComparison.Ordinal) ||
			value.CreatedUtc == default)
			throw new InvalidDataException(
				"Restoration workspace, session binding, and creation time are required.");
		if (value.Completed)
		{
			Hash(value.RestoredSnapshotSha256, "Restored snapshot hash");
			if (value.CompletedUtc == null || value.CompletedUtc == default)
				throw new InvalidDataException(
					"A completed baseline restoration requires a completion time.");
		}
		else if (!string.IsNullOrEmpty(value.RestoredSnapshotSha256) ||
			value.CompletedUtc != null)
			throw new InvalidDataException(
				"An incomplete restoration intent cannot claim completion evidence.");
	}

	public static void Validate(PolishPassStateRecord value)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Schema(value.SchemaVersion, PolishPassStateRecord.CurrentSchemaVersion, "state");
		Text(value.SessionId, "State session ID");
		Text(value.PlanId, "State plan ID");
		if (value.PlanRevision < 1 || value.Sequence < 1)
			throw new InvalidDataException("State plan revision and sequence must be positive.");
		Hash(value.PlanSha256, "State plan hash");
		Text(value.Reason, "State reason");
		if (value.RecordedUtc == default) throw new InvalidDataException("State time is required.");
	}

	private static void Approval(PolishPassApproval value, PolishPassKind pass,
		string sessionId, string planId, int revision, string hash)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		Schema(value.SchemaVersion, PolishPassApproval.CurrentSchemaVersion, "approval");
		if (value.Pass != pass || value.SessionId != sessionId || value.PlanId != planId ||
			value.PlanRevision != revision ||
			!string.Equals(value.PlanSha256, hash, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Approval does not target this exact plan revision.");
		Text(value.DecidedBy, "Approval actor");
		if (value.DecidedUtc == default) throw new InvalidDataException("Approval time is required.");
	}

	private static void Header(string sessionId, string planId, int revision,
		string baseHash, DateTimeOffset created)
	{
		Text(sessionId, "Session ID"); Text(planId, "Plan ID");
		if (revision < 1) throw new InvalidDataException("Plan revision must be positive.");
		Hash(baseHash, "Base rough-cut hash");
		if (created == default) throw new InvalidDataException("Plan creation time is required.");
	}
	private static void Capabilities(PolishRendererCapabilities value)
	{
		if (value == null || value.SchemaVersion != PolishRendererCapabilities.CurrentSchemaVersion ||
			value.UnsupportedVisualTreatments == null ||
			value.UnsupportedVisualTreatments.Any(string.IsNullOrWhiteSpace))
			throw new InvalidDataException("Renderer capabilities are invalid.");
	}
	private static void Diagnostics(IList<string> value)
	{
		if (value == null || value.Any(string.IsNullOrWhiteSpace))
			throw new InvalidDataException("Diagnostics are invalid.");
	}
	private static void Schema(int actual, int expected, string name)
	{
		if (actual != expected) throw new InvalidDataException($"Unsupported {name} schema {actual}.");
	}
	private static void Text(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException(name + " is required.");
	}
	private static void Hash(string value, string name)
	{
		if (value == null || value.Length != 64 || value.Any(x => !Uri.IsHexDigit(x)))
			throw new InvalidDataException(name + " is not a SHA-256 value.");
	}
	private static void Absolute(string value, string name)
	{
		Text(value, name);
		if (!Path.IsPathRooted(value)) throw new InvalidDataException(name + " must be absolute.");
	}
	private static void Relative(string value, string name)
	{
		Text(value, name);
		if (Path.IsPathRooted(value) ||
			value.Split('/', '\\').Any(segment => segment == ".."))
			throw new InvalidDataException(name + " must be a safe relative path.");
	}
	private static bool Finite(double value) =>
		!double.IsNaN(value) && !double.IsInfinity(value);
}
