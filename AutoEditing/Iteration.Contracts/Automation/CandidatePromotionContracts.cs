using System;
using System.Collections.Generic;
using System.Linq;
using AutoEditing.Iteration.Contracts.Serialization;
using Newtonsoft.Json.Linq;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class PromoteCandidateRequest
{
	public string PromotionId { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; }
	public string ExpectedCandidateSnapshotSha256 { get; set; } = "";
}

public sealed class CandidatePromotionTrackMapping
{
	public int CandidateTrackIndex { get; set; }
	public string MediaKind { get; set; } = "";
	public string CandidateName { get; set; } = "";
	public string FinalName { get; set; } = "";
}

public sealed class PromoteCandidateResult
{
	public string PromotionId { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; }
	public CandidateTimelineSnapshot CandidateSnapshot { get; set; }
	public string CandidateSnapshotSha256 { get; set; } = "";
	public CandidateTimelineSnapshot PromotedSnapshot { get; set; }
	public string PromotedSnapshotSha256 { get; set; } = "";
	public IList<CandidatePromotionTrackMapping> TrackMappings { get; set; } =
		new List<CandidatePromotionTrackMapping>();
}

public sealed class RollbackCandidatePromotionRequest
{
	public PromoteCandidateResult Promotion { get; set; }
}

public sealed class RollbackCandidatePromotionResult
{
	public string PromotionId { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; }
	public CandidateTimelineSnapshot RestoredSnapshot { get; set; }
	public string RestoredSnapshotSha256 { get; set; } = "";
}

/// <summary>
/// Pure promotion rules shared by the companion, tests, and VEGAS adapter.
/// The returned mapping is the complete mutation surface: promotion may rename
/// these tracks and must not touch any other project object.
/// </summary>
public static class CandidatePromotionContract
{
	public const string FinalVideoTrackName = "AE|Montage Clips";
	public const string FinalSongTrackName = "AE|Montage Song";
	public const string FinalSfxTrackPrefix = "AE|Montage Gun SFX ";

	public static IList<CandidatePromotionTrackMapping> Plan(
		CandidateTimelineSnapshot snapshot,
		IEnumerable<string> unrelatedTrackNames)
	{
		if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
		if (snapshot.Workspace == null)
			throw new InvalidOperationException("Promotion requires a candidate workspace.");
		snapshot.Workspace.Validate();
		if (snapshot.Tracks == null || snapshot.Tracks.Count == 0)
			throw new InvalidOperationException("Promotion requires at least one candidate track.");

		HashSet<string> reservedNames = new HashSet<string>(
			unrelatedTrackNames ?? throw new ArgumentNullException(nameof(unrelatedTrackNames)),
			StringComparer.OrdinalIgnoreCase);
		List<CandidatePromotionTrackMapping> mappings =
			new List<CandidatePromotionTrackMapping>();
		string prefix = snapshot.Workspace.OwnershipPrefix + "|";
		bool hasVideo = false;
		foreach (CandidateTrackSnapshot track in snapshot.Tracks)
		{
			if (track == null || string.IsNullOrWhiteSpace(track.Name) ||
				!track.Name.StartsWith(prefix, StringComparison.Ordinal))
				throw new InvalidOperationException(
					"Promotion snapshot contains a track outside the candidate workspace.");

			string suffix = track.Name.Substring(prefix.Length);
			string finalName;
			if (string.Equals(suffix, "VIDEO", StringComparison.Ordinal))
			{
				if (hasVideo)
					throw new InvalidOperationException(
						"Promotion snapshot contains more than one candidate video track.");
				hasVideo = true;
				finalName = FinalVideoTrackName;
			}
			else if (string.Equals(suffix, "SONG", StringComparison.Ordinal))
			{
				finalName = FinalSongTrackName;
			}
			else if (suffix.StartsWith("SFX|", StringComparison.Ordinal) &&
				int.TryParse(suffix.Substring(4), out int sfxIndex) &&
				sfxIndex > 0)
			{
				finalName = FinalSfxTrackPrefix + sfxIndex;
			}
			else
			{
				throw new InvalidOperationException(
					"Promotion encountered an unknown candidate track role: '" +
					track.Name + "'.");
			}

			if (!reservedNames.Add(finalName))
				throw new InvalidOperationException(
					"Promotion would overwrite or ambiguously reuse existing track '" +
					finalName + "'.");
			mappings.Add(new CandidatePromotionTrackMapping
			{
				CandidateTrackIndex = track.Index,
				MediaKind = track.MediaKind ?? "",
				CandidateName = track.Name,
				FinalName = finalName
			});
		}
		if (!hasVideo)
			throw new InvalidOperationException(
				"Promotion requires exactly one candidate video track.");
		return mappings;
	}

	public static void ValidateResult(PromoteCandidateResult result)
	{
		if (result == null) throw new ArgumentNullException(nameof(result));
		if (string.IsNullOrWhiteSpace(result.PromotionId))
			throw new InvalidOperationException("Promotion ID is required.");
		if (result.Workspace == null)
			throw new InvalidOperationException("Promotion workspace is required.");
		result.Workspace.Validate();
		if (result.CandidateSnapshot == null || result.PromotedSnapshot == null)
			throw new InvalidOperationException(
				"Promotion must retain both candidate and promoted snapshots.");
		if (result.TrackMappings == null ||
			result.TrackMappings.Count != result.CandidateSnapshot.Tracks.Count)
			throw new InvalidOperationException(
				"Promotion track mappings do not cover the candidate snapshot.");
		if (string.IsNullOrWhiteSpace(result.CandidateSnapshotSha256) ||
			string.IsNullOrWhiteSpace(result.PromotedSnapshotSha256))
			throw new InvalidOperationException("Promotion snapshot hashes are required.");
		if (!SameWorkspace(result.Workspace, result.CandidateSnapshot.Workspace) ||
			!SameWorkspace(result.Workspace, result.PromotedSnapshot.Workspace))
			throw new InvalidOperationException(
				"Promotion snapshots do not identify the promoted workspace.");
		if (!string.Equals(
			SnapshotHash(result.CandidateSnapshot),
			result.CandidateSnapshotSha256,
			StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(
			SnapshotHash(result.PromotedSnapshot),
			result.PromotedSnapshotSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Promotion snapshot content does not match its SHA-256.");
		string prefix = result.Workspace.OwnershipPrefix + "|";
		foreach (CandidateTrackSnapshot track in result.PromotedSnapshot.Tracks)
		{
			if (track.Name != null &&
				track.Name.StartsWith(prefix, StringComparison.Ordinal))
				throw new InvalidOperationException(
					"Promoted timeline still contains a candidate-only track label.");
		}
		foreach (CandidatePromotionTrackMapping mapping in result.TrackMappings)
		{
			CandidateTrackSnapshot before = result.CandidateSnapshot.Tracks
				.SingleOrDefault(track => string.Equals(
					track.Name, mapping.CandidateName, StringComparison.Ordinal))
				?? throw new InvalidOperationException(
					"Promotion mapping does not resolve its candidate track.");
			CandidateTrackSnapshot after = result.PromotedSnapshot.Tracks
				.SingleOrDefault(track => string.Equals(
					track.Name, mapping.FinalName, StringComparison.Ordinal))
				?? throw new InvalidOperationException(
					"Promotion mapping does not resolve its final track.");
			if (!string.Equals(
				TrackContentHash(before),
				TrackContentHash(after),
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"Promotion changed candidate track content instead of only its label.");
		}
	}

	public static void ValidateRollbackSnapshot(
		PromoteCandidateResult promotion,
		CandidateTimelineSnapshot currentPromotedSnapshot)
	{
		ValidateResult(promotion);
		if (currentPromotedSnapshot == null)
			throw new ArgumentNullException(nameof(currentPromotedSnapshot));
		if (!SameWorkspace(
			promotion.Workspace,
			currentPromotedSnapshot.Workspace) ||
			!string.Equals(
				SnapshotHash(currentPromotedSnapshot),
				promotion.PromotedSnapshotSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Promotion rollback stopped because the promoted timeline diverged " +
				"from its recoverable backup.");
	}

	/// <summary>
	/// Accepts a complete promotion or an interrupted rename transaction. Every
	/// mapped track must exist exactly once under either its candidate or final
	/// name and its content must still equal the backup.
	/// </summary>
	public static void ValidateRecoverableTrackSet(
		PromoteCandidateResult promotion,
		CandidateTimelineSnapshot liveSnapshot)
	{
		ValidateResult(promotion);
		if (liveSnapshot == null)
			throw new ArgumentNullException(nameof(liveSnapshot));
		if (!SameWorkspace(promotion.Workspace, liveSnapshot.Workspace))
			throw new InvalidOperationException(
				"Promotion recovery snapshot identifies another workspace.");
		if (liveSnapshot.Tracks.Count != promotion.TrackMappings.Count)
			throw new InvalidOperationException(
				"Promotion recovery cannot account for every mapped track.");
		foreach (CandidatePromotionTrackMapping mapping in promotion.TrackMappings)
		{
			List<CandidateTrackSnapshot> matches = liveSnapshot.Tracks
				.Where(track =>
					string.Equals(
						track.Name, mapping.CandidateName, StringComparison.Ordinal) ||
					string.Equals(
						track.Name, mapping.FinalName, StringComparison.Ordinal))
				.ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException(
					"Promotion recovery cannot identify exactly one mapped track.");
			CandidateTrackSnapshot backup = promotion.CandidateSnapshot.Tracks
				.Single(track => string.Equals(
					track.Name, mapping.CandidateName, StringComparison.Ordinal));
			if (!string.Equals(
				TrackContentHash(backup),
				TrackContentHash(matches[0]),
				StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"Promotion recovery stopped because mapped track content diverged.");
		}
	}

	public static void ValidateRestoredSnapshot(
		PromoteCandidateResult promotion,
		CandidateTimelineSnapshot restoredSnapshot)
	{
		ValidateResult(promotion);
		if (restoredSnapshot == null)
			throw new ArgumentNullException(nameof(restoredSnapshot));
		if (!SameWorkspace(promotion.Workspace, restoredSnapshot.Workspace) ||
			!string.Equals(
				SnapshotHash(restoredSnapshot),
				promotion.CandidateSnapshotSha256,
				StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Promotion rollback did not reproduce the candidate backup exactly.");
	}

	private static bool SameWorkspace(
		CandidateWorkspaceId left,
		CandidateWorkspaceId right) =>
		left != null && right != null &&
		string.Equals(left.SessionId, right.SessionId, StringComparison.Ordinal) &&
		left.Iteration == right.Iteration &&
		string.Equals(left.Nonce, right.Nonce, StringComparison.Ordinal);

	private static string SnapshotHash(CandidateTimelineSnapshot snapshot) =>
		ContractHash.Compute(JToken.FromObject(snapshot));

	private static string TrackContentHash(CandidateTrackSnapshot track)
	{
		JObject value = JObject.FromObject(track);
		value["Name"] = "";
		return ContractHash.Compute(value);
	}
}
