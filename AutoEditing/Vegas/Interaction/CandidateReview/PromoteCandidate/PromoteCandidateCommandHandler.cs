using System;
using System.Collections.Generic;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class PromoteCandidateCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.PromoteCandidate;

	public string Execute(Vegas vegas, string payloadJson)
	{
		PromoteCandidateCommand command =
			JsonConvert.DeserializeObject<PromoteCandidateCommand>(payloadJson);
		PromoteCandidateRequest request = command?.Request
			?? throw new InvalidOperationException("Candidate promotion request is empty.");
		VegasContractValidator.Validate(request);

		List<Track> owned =
			CandidateWorkspaceDiscovery.FindOwnedTracks(vegas.Project, request.Workspace);
		CandidateTimelineSnapshot before =
			CandidateSnapshotReader.ReadTracks(request.Workspace, owned);
		string beforeHash = SnapshotHash(before);
		if (!string.Equals(
			beforeHash,
			request.ExpectedCandidateSnapshotSha256,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Candidate promotion stopped because the live candidate diverged " +
				"from the validated final snapshot.");

		HashSet<Track> ownedSet = new HashSet<Track>(owned);
		IList<CandidatePromotionTrackMapping> mappings =
			CandidatePromotionContract.Plan(
				before,
				((IEnumerable<Track>)vegas.Project.Tracks)
					.Where(track => !ownedSet.Contains(track))
					.Select(track => track.Name));
		Dictionary<string, Track> byCandidateName = owned.ToDictionary(
			track => track.Name,
			StringComparer.Ordinal);
		List<CandidatePromotionTrackMapping> applied =
			new List<CandidatePromotionTrackMapping>();
		try
		{
			foreach (CandidatePromotionTrackMapping mapping in mappings)
			{
				Track track;
				if (!byCandidateName.TryGetValue(mapping.CandidateName, out track))
					throw new InvalidOperationException(
						"Candidate promotion track disappeared before mutation: '" +
						mapping.CandidateName + "'.");
				track.Name = mapping.FinalName;
				applied.Add(mapping);
			}
			CandidateTimelineSnapshot promoted =
				CandidateSnapshotReader.ReadTracks(request.Workspace, owned);
			PromoteCandidateResult result = new PromoteCandidateResult
			{
				PromotionId = request.PromotionId,
				Workspace = request.Workspace,
				CandidateSnapshot = before,
				CandidateSnapshotSha256 = beforeHash,
				PromotedSnapshot = promoted,
				PromotedSnapshotSha256 = SnapshotHash(promoted),
				TrackMappings = mappings
			};
			CandidatePromotionContract.ValidateResult(result);
			return JsonConvert.SerializeObject(result);
		}
		catch
		{
			for (int index = applied.Count - 1; index >= 0; index--)
			{
				CandidatePromotionTrackMapping mapping = applied[index];
				Track track = owned.FirstOrDefault(item =>
					string.Equals(item.Name, mapping.FinalName, StringComparison.Ordinal));
				if (track != null)
					track.Name = mapping.CandidateName;
			}
			throw;
		}
	}

	private static string SnapshotHash(CandidateTimelineSnapshot snapshot) =>
		ContractHash.Compute(JToken.FromObject(snapshot));
}
