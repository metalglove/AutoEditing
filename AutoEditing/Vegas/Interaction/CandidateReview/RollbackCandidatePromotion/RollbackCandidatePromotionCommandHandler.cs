using System;
using System.Collections.Generic;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class RollbackCandidatePromotionCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.RollbackCandidatePromotion;

	public string Execute(Vegas vegas, string payloadJson)
	{
		RollbackCandidatePromotionCommand command =
			JsonConvert.DeserializeObject<RollbackCandidatePromotionCommand>(payloadJson);
		PromoteCandidateResult promotion = command?.Request?.Promotion
			?? throw new InvalidOperationException("Candidate rollback request is empty.");
		VegasContractValidator.Validate(command.Request);

		List<Track> promotedTracks = new List<Track>();
		HashSet<Track> selected = new HashSet<Track>();
		foreach (CandidatePromotionTrackMapping mapping in promotion.TrackMappings)
		{
			List<Track> matches = ((IEnumerable<Track>)vegas.Project.Tracks)
				.Where(track =>
					string.Equals(
						track.Name, mapping.FinalName, StringComparison.Ordinal) ||
					string.Equals(
						track.Name, mapping.CandidateName, StringComparison.Ordinal))
				.ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException(
					"Promotion rollback cannot identify exactly one candidate or final track for '" +
					mapping.FinalName + "'.");
			if (!selected.Add(matches[0]))
				throw new InvalidOperationException(
					"Promotion rollback mappings resolve to the same live track.");
			promotedTracks.Add(matches[0]);
		}

		CandidateTimelineSnapshot current = CandidateSnapshotReader.ReadTracks(
			promotion.Workspace,
			promotedTracks);
		CandidatePromotionContract.ValidateRecoverableTrackSet(promotion, current);

		HashSet<Track> promotedSet = new HashSet<Track>(promotedTracks);
		HashSet<string> unrelatedNames = new HashSet<string>(
			((IEnumerable<Track>)vegas.Project.Tracks)
				.Where(track => !promotedSet.Contains(track))
				.Select(track => track.Name),
			StringComparer.OrdinalIgnoreCase);
		foreach (CandidatePromotionTrackMapping mapping in promotion.TrackMappings)
		{
			Track selectedTrack = promotedTracks.Single(item =>
				string.Equals(item.Name, mapping.FinalName, StringComparison.Ordinal) ||
				string.Equals(item.Name, mapping.CandidateName, StringComparison.Ordinal));
			if (string.Equals(
				selectedTrack.Name,
				mapping.FinalName,
				StringComparison.Ordinal) &&
				unrelatedNames.Contains(mapping.CandidateName))
				throw new InvalidOperationException(
					"Promotion rollback would collide with unrelated track '" +
					mapping.CandidateName + "'.");
		}

		List<CandidatePromotionTrackMapping> applied =
			new List<CandidatePromotionTrackMapping>();
		try
		{
			foreach (CandidatePromotionTrackMapping mapping in promotion.TrackMappings)
			{
				Track track = promotedTracks.Single(item =>
					string.Equals(item.Name, mapping.FinalName, StringComparison.Ordinal) ||
					string.Equals(item.Name, mapping.CandidateName, StringComparison.Ordinal));
				if (string.Equals(
					track.Name,
					mapping.CandidateName,
					StringComparison.Ordinal))
					continue;
				track.Name = mapping.CandidateName;
				applied.Add(mapping);
			}
		}
		catch
		{
			for (int index = applied.Count - 1; index >= 0; index--)
			{
				CandidatePromotionTrackMapping mapping = applied[index];
				Track track = promotedTracks.First(item =>
					string.Equals(item.Name, mapping.CandidateName, StringComparison.Ordinal));
				track.Name = mapping.FinalName;
			}
			throw;
		}

		CandidateTimelineSnapshot restored = CandidateSnapshotReader.Read(
			vegas.Project,
			promotion.Workspace);
		string restoredHash = SnapshotHash(restored);
		try
		{
			CandidatePromotionContract.ValidateRestoredSnapshot(
				promotion,
				restored);
		}
		catch
		{
			// Do not leave the project in an unverified half-state.
			foreach (CandidatePromotionTrackMapping mapping in promotion.TrackMappings)
			{
				if (!applied.Contains(mapping))
					continue;
				Track track = promotedTracks.Single(item =>
					string.Equals(item.Name, mapping.CandidateName, StringComparison.Ordinal));
				track.Name = mapping.FinalName;
			}
			throw;
		}

		return JsonConvert.SerializeObject(new RollbackCandidatePromotionResult
		{
			PromotionId = promotion.PromotionId,
			Workspace = promotion.Workspace,
			RestoredSnapshot = restored,
			RestoredSnapshotSha256 = restoredHash
		});
	}

	private static string SnapshotHash(CandidateTimelineSnapshot snapshot) =>
		ContractHash.Compute(JToken.FromObject(snapshot));
}
