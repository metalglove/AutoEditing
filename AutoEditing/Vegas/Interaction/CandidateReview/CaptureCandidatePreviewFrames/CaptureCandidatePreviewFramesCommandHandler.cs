using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Automation;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class CaptureCandidatePreviewFramesCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.CaptureCandidatePreviewFrames;

	public string Execute(Vegas vegas, string payloadJson)
	{
		CaptureCandidatePreviewFramesCommand command =
			JsonConvert.DeserializeObject<CaptureCandidatePreviewFramesCommand>(payloadJson);
		CaptureCandidatePreviewFramesRequest request = command?.Request;
		if (request == null)
			throw new InvalidOperationException(
				"Candidate preview-frame request is empty.");
		VegasContractValidator.Validate(request);
		List<Track> owned =
			CandidateWorkspaceDiscovery.FindOwnedTracks(vegas.Project, request.Workspace);
		if (owned.Count == 0)
			throw new InvalidOperationException("The candidate workspace does not exist.");
		CandidateWorkspaceOwnershipValidator.ValidateOwnedTrackNames(
			request.Workspace, owned.Select(track => track.Name));
		CandidateTimelineSnapshot snapshot =
			CandidateSnapshotReader.Read(vegas.Project, request.Workspace);
		foreach (TimeSpan time in request.TimelineTimes)
			if (time < snapshot.TimelineStart || time > snapshot.TimelineEnd)
				throw new InvalidOperationException(
					"A requested preview frame is outside the candidate workspace.");

		string sessionRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"AutoEditing", "automation", "sessions", request.Workspace.SessionId);
		PreviewRenderStateSnapshot captured = CandidatePreviewIsolation.Capture(vegas);
		PreviewRenderStatePlan isolation = PreviewRenderStatePlanner.Create(
			captured,
			owned.Select(CandidatePreviewIsolation.StableTrackKey));
		List<CapturedCandidatePreviewFrame> frames =
			new List<CapturedCandidatePreviewFrame>();
		try
		{
			CandidatePreviewIsolation.Apply(
				vegas.Project, isolation.IsolateCandidate);
			for (int index = 0; index < request.TimelineTimes.Count; index++)
			{
				string relativePath =
					request.OutputDirectoryRelativePath + "/frame-" +
					(index + 1).ToString("D4") + ".png";
				string outputPath = PreviewRenderPlanFactory.ResolveContainedOutput(
					sessionRoot, relativePath);
				Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
				if (File.Exists(outputPath))
					throw new InvalidOperationException(
						"Preview frame output already exists.");
				TimeSpan time = request.TimelineTimes[index];
				RenderStatus status = vegas.SaveSnapshot(
					outputPath,
					ImageFileFormat.PNG,
					Timecode.FromSeconds(time.TotalSeconds));
				if (status != RenderStatus.Complete)
					throw new InvalidOperationException(
						"VEGAS failed to capture preview frame " +
						(index + 1) + " (" + status + ").");
				if (!File.Exists(outputPath))
					throw new InvalidOperationException(
						"VEGAS did not produce a requested preview frame.");
				frames.Add(new CapturedCandidatePreviewFrame
				{
					TimelineTime = time,
					OutputRelativePath = relativePath,
					Sha256 = ComputeSha256(outputPath)
				});
			}
			return JsonConvert.SerializeObject(
				new CaptureCandidatePreviewFramesResult { Frames = frames });
		}
		finally
		{
			CandidatePreviewIsolation.Restore(vegas, captured);
		}
	}

	private static string ComputeSha256(string path)
	{
		using (FileStream stream = File.OpenRead(path))
		using (SHA256 sha = SHA256.Create())
			return BitConverter.ToString(sha.ComputeHash(stream))
				.Replace("-", "").ToLowerInvariant();
	}
}
