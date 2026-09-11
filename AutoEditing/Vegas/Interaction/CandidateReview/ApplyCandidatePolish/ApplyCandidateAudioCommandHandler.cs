using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AutoEditing.Iteration.Contracts.Assembly;
using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Serialization;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Editing;
using Core.Domain.Planning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class ApplyCandidateAudioCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.ApplyCandidateAudio;

	public string Execute(Vegas vegas, string payloadJson)
	{
		ApplyCandidateAudioCommand command =
			JsonConvert.DeserializeObject<ApplyCandidateAudioCommand>(payloadJson);
		ApplyCandidateAudioRequest request = command?.Request ??
			throw new InvalidOperationException("Audio-pass request is empty.");
		ShotDetectionConfig config = ConfigurationManager.GetShotDetection();
		SfxTemplateCatalog catalog = ValidateRequest(vegas.Project, request, config);
		Track existingSong = FindExistingSongTrack(
			vegas.Project,
			request.Workspace);
		bool reuseSong = request.Audio.Song != null && existingSong != null;
		if (reuseSong)
			CandidateAudioReuseContract.ValidateSongTrack(
				CandidateSnapshotReader.ReadTracks(
						request.Workspace,
						new[] { existingSong })
					.Tracks.Single(),
				request.Audio.Song);
		MontageBuildContext context = MontageBuildContext.Candidate(
			request.Workspace,
			applyEffects: false,
			includeSong: request.Audio.Song != null && !reuseSong,
			includeSfx: request.Audio.Sfx.Count != 0);
		AudioBuildArtifacts artifacts = null;
		try
		{
			artifacts = new MontageAudioBuilder().Build(
				vegas.Project,
				request.Plan.Montage.Placements,
				request.Audio.Song?.SongPath ?? "",
				config.SfxRoot,
				catalog,
				context);
			List<PolishActionResult> actions = new List<PolishActionResult>();
			if (request.Audio.Song != null)
				actions.Add(new PolishActionResult
				{
					ActionId = request.Audio.Song.ActionId,
					Outcome = PolishActionOutcome.Applied,
					Detail = reuseSong
						? "Existing synchronization song track was verified at 0.5 gain from timeline zero."
						: "Song track rendered at 0.5 gain from timeline zero."
				});
			actions.AddRange(request.Audio.Sfx.Select(item => new PolishActionResult
			{
				ActionId = item.ActionId,
				Outcome = PolishActionOutcome.Applied,
				Detail = "Reviewed gun/hit SFX aligned to its confirmation anchor."
			}));
			int expectedEvents = request.Audio.Sfx.Count +
				(request.Audio.Song != null && !reuseSong ? 1 : 0);
			if (artifacts.Events.Count != expectedEvents)
				throw new InvalidOperationException(
					$"Audio pass planned {expectedEvents} actions but VEGAS created " +
					$"{artifacts.Events.Count} events.");
			PolishPassMaterialization materialization = new PolishPassMaterialization
			{
				SessionId = request.Audio.SessionId,
				Pass = PolishPassKind.Audio,
				PlanId = request.Audio.PlanId,
				PlanRevision = request.Audio.Revision,
				PlanSha256 = request.PlanSha256.ToLowerInvariant(),
				Workspace = request.Workspace,
				CompletedUtc = DateTimeOffset.UtcNow,
				FullyApplied = true,
				Actions = actions,
				Snapshot = CandidateSnapshotReader.Read(vegas.Project, request.Workspace)
			};
			PolishPassContractValidator.Validate(materialization);
			return JsonConvert.SerializeObject(new ApplyCandidateAudioResult
			{
				Materialization = materialization
			});
		}
		catch
		{
			if (artifacts != null)
			{
				for (int index = artifacts.Tracks.Count - 1; index >= 0; index--)
					((BaseList<Track>)(object)vegas.Project.Tracks)
						.Remove(artifacts.Tracks[index]);
			}
			throw;
		}
	}

	private static SfxTemplateCatalog ValidateRequest(
		Project project,
		ApplyCandidateAudioRequest request,
		ShotDetectionConfig config)
	{
		request.Workspace?.Validate();
		if (request.Workspace == null || request.Plan == null || request.Audio == null)
			throw new InvalidOperationException(
				"Audio-pass workspace, rough cut, and plan are required.");
		EditPlanDocumentValidator.ValidateAndNormalize(request.Plan);
		PolishPassContractValidator.Validate(request.Audio);
		string documentHash;
		using (SHA256 sha = SHA256.Create())
			documentHash = BitConverter.ToString(sha.ComputeHash(
				new UTF8Encoding(false).GetBytes(
					EditPlanDocumentSerializer.SerializePlan(request.Plan))))
				.Replace("-", "").ToLowerInvariant();
		if (!string.Equals(request.Audio.BaseRoughCutSha256, documentHash,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Audio pass does not target the supplied accepted rough cut.");
		string audioHash = ContractHash.Compute(
			JToken.Parse(ContractSerializer.Serialize(request.Audio)));
		if (!string.Equals(request.PlanSha256, audioHash,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Audio-pass hash does not match the exact plan payload.");

		List<Track> owned = CandidateWorkspaceDiscovery.FindOwnedTracks(
			project, request.Workspace);
		if (!owned.Any(track => track.MediaType == MediaType.Video))
			throw new InvalidOperationException("The candidate video workspace is missing.");
		if (owned.Any(track =>
			track.Name.StartsWith(
				request.Workspace.OwnershipPrefix + "|SFX|",
				StringComparison.Ordinal)))
			throw new InvalidOperationException(
				"Candidate SFX tracks already exist. Reapplying an audio pass " +
				"requires explicit rollback first.");
		if (request.Audio.Song != null && !File.Exists(request.Audio.Song.SongPath))
			throw new FileNotFoundException(
				"The planned song file is missing.", request.Audio.Song.SongPath);

		List<(string Path, int Index, double Time, string Gun,
			ShotOutcome Outcome, string Template)> expected = new();
		foreach (ClipPlacement placement in request.Plan.Montage.Placements
			.OrderBy(item => item.TimelineStartSeconds)
			.ThenBy(item => item.Clip.FilePath, StringComparer.OrdinalIgnoreCase))
		{
			List<TimelineShotEvent> kills = placement.TimelineShotEvents
				.Where(item => item.SourceEvent.IsConfirmedKill)
				.OrderBy(item => item.TimelineTimeSeconds)
				.ToList();
			for (int index = 0; index < kills.Count; index++)
				expected.Add((Path.GetFullPath(placement.Clip.FilePath), index,
					kills[index].TimelineTimeSeconds,
					kills[index].SourceEvent.Gun ?? placement.Clip.Gun,
					kills[index].SourceEvent.Outcome,
					kills[index].SourceEvent.TemplateId ?? ""));
		}
		if (request.Audio.Sfx.Count != expected.Count)
			throw new InvalidOperationException(
				"Audio pass must represent every reviewed confirmed kill exactly once.");
		for (int index = 0; index < expected.Count; index++)
		{
			AudioPassSfxAction actual = request.Audio.Sfx[index];
			(string path, int killIndex, double time, string gun,
				ShotOutcome outcome, string template) = expected[index];
			if (!string.Equals(Path.GetFullPath(actual.PlacementPath), path,
					StringComparison.OrdinalIgnoreCase) ||
				actual.ConfirmedKillIndex != killIndex ||
				Math.Abs(actual.ConfirmationTimeSeconds - time) > 0.001 ||
				!string.Equals(GunNameNormalizer.Resolve(actual.Gun),
					GunNameNormalizer.Resolve(gun), StringComparison.Ordinal) ||
				actual.Outcome != outcome ||
				!string.Equals(actual.PreferredTemplateId ?? "", template,
					StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(
					"An audio SFX action differs from the canonical reviewed kill.");
		}
		if (expected.Count == 0) return new SfxTemplateCatalog();
		SfxTemplateCatalog catalog = SfxTemplateCatalog.Load(config.SfxRoot);
		foreach (string gun in expected.Select(item => item.Gun)
			.Distinct(StringComparer.OrdinalIgnoreCase))
			catalog.ValidateForGun(config.SfxRoot, gun);
		return catalog;
	}

	private static Track FindExistingSongTrack(
		Project project,
		CandidateWorkspaceId workspace)
	{
		List<Track> matches = CandidateWorkspaceDiscovery.FindOwnedTracks(
				project,
				workspace)
			.Where(track => string.Equals(
				track.Name,
				CandidateTrackNaming.Song(workspace),
				StringComparison.Ordinal))
			.ToList();
		if (matches.Count > 1)
			throw new InvalidOperationException(
				"The candidate contains multiple owned song tracks.");
		return matches.SingleOrDefault();
	}

}
