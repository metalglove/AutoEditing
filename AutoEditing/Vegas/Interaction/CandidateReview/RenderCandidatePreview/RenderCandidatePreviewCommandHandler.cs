using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AutoEditing.Iteration.Contracts.Automation;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class RenderCandidatePreviewCommandHandler : IVegasCommandHandler
{
	public string CommandType => VegasOperations.RenderCandidatePreview;

	public string Execute(Vegas vegas, string payloadJson)
	{
		RenderCandidatePreviewCommand command =
			JsonConvert.DeserializeObject<RenderCandidatePreviewCommand>(payloadJson);
		RenderCandidatePreviewRequest request = command?.Request;
		if (request == null) throw new InvalidOperationException("Candidate preview request is empty.");
		VegasContractValidator.Validate(request);

		List<Track> owned = CandidateWorkspaceDiscovery.FindOwnedTracks(vegas.Project, request.Workspace);
		if (owned.Count == 0)
			throw new InvalidOperationException("The candidate workspace does not exist.");
		CandidateWorkspaceOwnershipValidator.ValidateOwnedTrackNames(
			request.Workspace, owned.Select(track => track.Name));
		CandidateTimelineSnapshot snapshot =
			CandidateSnapshotReader.Read(vegas.Project, request.Workspace);

		List<Renderer> renderers = ((IEnumerable<Renderer>)vegas.Renderers).ToList();
		List<PreviewRendererCapability> capabilities = renderers.Select(ToCapability).ToList();
		PreviewRenderProfile profile =
			PreviewRenderProfileResolver.Resolve(request.RenderProfileId, capabilities);
		string sessionRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"AutoEditing", "automation", "sessions", request.Workspace.SessionId);
		PreviewRenderPlan plan = PreviewRenderPlanFactory.Create(
			sessionRoot,
			request.OutputRelativePath,
			snapshot.TimelineStart.TotalSeconds,
			snapshot.TimelineEnd.TotalSeconds,
			request.Start.TotalSeconds,
			request.Duration.TotalSeconds,
			profile,
			capabilities);
		Renderer renderer = renderers.Single(item =>
			string.Equals(item.ClassID.ToString("D"), plan.Renderer.Renderer.ClassId, StringComparison.OrdinalIgnoreCase));
		RenderTemplate template = ((IEnumerable<RenderTemplate>)renderer.Templates).Single(item =>
			string.Equals(item.TemplateGuid.ToString("D"), plan.Renderer.Template.TemplateId, StringComparison.OrdinalIgnoreCase));

		PreviewRenderStateSnapshot captured = CandidatePreviewIsolation.Capture(vegas);
		PreviewRenderStatePlan state = PreviewRenderStatePlanner.Create(
			captured, owned.Select(CandidatePreviewIsolation.StableTrackKey));
		try
		{
			CandidatePreviewIsolation.Apply(vegas.Project, state.IsolateCandidate);
			vegas.SelectionStart = Timecode.FromSeconds(plan.StartSeconds);
			vegas.SelectionLength = Timecode.FromSeconds(plan.DurationSeconds);
			vegas.LoopPlayback = false;
			Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputPath));
			if (File.Exists(plan.OutputPath))
				throw new InvalidOperationException("Preview output already exists.");
			RenderArgs args = new RenderArgs(vegas.Project)
			{
				OutputFile = plan.OutputPath,
				RenderTemplate = template,
				Start = Timecode.FromSeconds(plan.StartSeconds),
				Length = Timecode.FromSeconds(plan.DurationSeconds),
				UseSelection = false,
				IncludeMarkers = false,
				WaitForIdle = true,
				ShowOpenButtonsOnComplete = false
			};
			RenderStatus status = vegas.Project.Render(args);
			if (status != RenderStatus.Complete)
				throw new InvalidOperationException("VEGAS preview render did not complete: " + status + ".");
			if (!File.Exists(plan.OutputPath))
				throw new InvalidOperationException("VEGAS reported success without producing the preview file.");
			return JsonConvert.SerializeObject(new RenderCandidatePreviewResult
			{
				OutputRelativePath = request.OutputRelativePath,
				Sha256 = ComputeSha256(plan.OutputPath),
				RenderedDuration = request.Duration,
				RenderProfileId = profile.Id
			});
		}
		finally
		{
			CandidatePreviewIsolation.Restore(vegas, captured);
		}
	}

	private static PreviewRendererCapability ToCapability(Renderer renderer) =>
		new PreviewRendererCapability(
			renderer.ClassID.ToString("D"),
			renderer.Name,
			((IEnumerable<RenderTemplate>)renderer.Templates).Select(template =>
				new PreviewTemplateCapability(
					template.TemplateGuid.ToString("D"),
					template.Name,
					template.IsValid(),
					template.VideoStreamCount,
					template.FileExtensions)));

	private static string ComputeSha256(string path)
	{
		using (FileStream stream = File.OpenRead(path))
		using (SHA256 sha = SHA256.Create())
			return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
	}
}
