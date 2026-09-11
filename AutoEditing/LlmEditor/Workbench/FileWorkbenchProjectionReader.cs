using AutoEditing.Iteration.Contracts.Automation;
using AutoEditing.Iteration.Contracts.Diff;
using AutoEditing.Iteration.Contracts.Iterations;
using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Sessions;
using AutoEditing.Iteration.Contracts.Steering;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Workbench;

public sealed class FileWorkbenchProjectionReader : IWorkbenchProjectionReader
{
	public WorkbenchSession Read(string sessionRoot)
	{
		SessionPathResolver paths = new(sessionRoot);
		EditSessionManifest manifest = ReadRequired<EditSessionManifest>(
			paths.Resolve("manifest.json"), "session manifest");
		List<WorkbenchDiagnostic> diagnostics = new();
		List<WorkbenchIteration> iterations = new();
		string iterationsRoot = paths.Resolve("iterations");

		if (Directory.Exists(iterationsRoot))
		{
			foreach (string directory in Directory.EnumerateDirectories(iterationsRoot)
				.OrderBy(path => path, StringComparer.Ordinal))
			{
				string name = Path.GetFileName(directory);
				if (!int.TryParse(name, out int number))
				{
					diagnostics.Add(new($"iterations/{name}", "Ignored: directory name is not an iteration number."));
					continue;
				}

				string snapshotPath = Path.Combine(directory, "snapshot.json");
				if (!File.Exists(snapshotPath))
				{
					diagnostics.Add(new($"iterations/{name}", "Ignored: snapshot.json is missing."));
					continue;
				}

				try
				{
					EditIterationSnapshot snapshot = ReadRequired<EditIterationSnapshot>(
						snapshotPath, $"iteration {number} snapshot");
					CandidateTimelineSnapshot? timeline = ReadOptional<CandidateTimelineSnapshot>(
						Path.Combine(directory, "timeline.json"), diagnostics, $"iterations/{name}/timeline.json");
					EditPlanDiff? diff = ReadOptional<EditPlanDiff>(
						Path.Combine(directory, "diff.json"), diagnostics, $"iterations/{name}/diff.json");
					bool blocking = snapshot.Findings.Any(finding =>
						finding.Severity == EditReviewSeverity.Error);
					bool preview = snapshot.Evidence.Any(evidence =>
						evidence.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
						evidence.MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase));

					iterations.Add(new(
						number,
						snapshot.CreatedUtc,
						snapshot.CandidateHash,
						snapshot.Candidate?.Montage?.Placements?.Count ?? 0,
						TimeSpan.FromSeconds(snapshot.Candidate?.Montage?.Placements?
							.Select(placement => placement.TimelineEndSeconds)
							.DefaultIfEmpty(0).Max() ?? 0),
						snapshot.Decisions.ToArray(),
						snapshot.Findings.ToArray(),
						snapshot.Evidence.ToArray(),
						timeline,
						diff,
						new(
							snapshot.Candidate != null,
							timeline != null,
							preview,
							blocking,
							number == manifest.CurrentIteration,
							manifest.State == EditSessionState.Accepted &&
								StringComparer.Ordinal.Equals(snapshot.CandidateHash, manifest.AcceptedPlanHash))));
				}
				catch (Exception exception) when (exception is IOException ||
					exception is Newtonsoft.Json.JsonException)
				{
					diagnostics.Add(new($"iterations/{name}/snapshot.json", exception.Message));
				}
			}
		}

		List<EditSteeringDirective> steering = new();
		string steeringRoot = paths.Resolve("steering");
		if (Directory.Exists(steeringRoot))
		{
			foreach (string file in Directory.EnumerateFiles(steeringRoot, "*.json")
				.OrderBy(path => path, StringComparer.Ordinal))
			{
				EditSteeringDirective? directive = ReadOptional<EditSteeringDirective>(
					file, diagnostics, paths.GetRelativePath(file));
				if (directive != null) steering.Add(directive);
			}
		}

		return new(
			manifest.SessionId,
			manifest.State,
			manifest.Revision,
			manifest.CurrentIteration,
			manifest.AcceptedPlanHash,
			iterations.OrderBy(item => item.Number).ToArray(),
			steering.OrderBy(item => item.ApplicableFromIteration)
				.ThenBy(item => item.DirectiveId, StringComparer.Ordinal).ToArray(),
			diagnostics);
	}

	private static T ReadRequired<T>(string path, string description)
	{
		if (!File.Exists(path)) throw new FileNotFoundException($"The {description} is missing.", path);
		return ContractSerializer.Deserialize<T>(File.ReadAllText(path));
	}

	private static T? ReadOptional<T>(
		string path,
		ICollection<WorkbenchDiagnostic> diagnostics,
		string artifact) where T : class
	{
		if (!File.Exists(path)) return null;
		try
		{
			return ContractSerializer.Deserialize<T>(File.ReadAllText(path));
		}
		catch (Exception exception) when (exception is IOException ||
			exception is Newtonsoft.Json.JsonException)
		{
			diagnostics.Add(new(artifact, exception.Message));
			return null;
		}
	}
}
