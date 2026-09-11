using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.Iteration.Contracts.Assembly;

public sealed class SectionMilestoneRenderManifest
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string SectionId { get; set; } = "";
	public int CompletedCheckpoint { get; set; }
	public CandidateWorkspaceId Workspace { get; set; }
	public string PlanSha256 { get; set; } = "";
	public string RenderProfileId { get; set; } = "";
	public TimeSpan TimelineStart { get; set; }
	public TimeSpan TimelineEnd { get; set; }
	public IList<RoughCutRenderChunk> Chunks { get; set; } =
		new List<RoughCutRenderChunk>();
	public DateTimeOffset CompletedUtc { get; set; }
}

public static class SectionMilestoneRenderContractValidator
{
	private static readonly TimeSpan MaximumChunkDuration =
		TimeSpan.FromSeconds(20);
	private static readonly TimeSpan BoundaryTolerance =
		TimeSpan.FromMilliseconds(2);

	public static void Validate(SectionMilestoneRenderManifest value)
	{
		if (value == null) throw new ArgumentNullException(nameof(value));
		if (value.SchemaVersion !=
			SectionMilestoneRenderManifest.CurrentSchemaVersion ||
			string.IsNullOrWhiteSpace(value.SessionId) ||
			string.IsNullOrWhiteSpace(value.SectionId) ||
			value.CompletedCheckpoint < 1 ||
			value.Workspace == null ||
			string.IsNullOrWhiteSpace(value.RenderProfileId) ||
			value.CompletedUtc == default)
			throw new InvalidDataException(
				"The section milestone render manifest is incomplete.");
		value.Workspace.Validate();
		if (value.PlanSha256 == null ||
			value.PlanSha256.Length != 64 ||
			value.PlanSha256.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException(
				"The section milestone plan hash is invalid.");
		if (value.TimelineStart < TimeSpan.Zero ||
			value.TimelineEnd <= value.TimelineStart ||
			value.Chunks == null ||
			value.Chunks.Count == 0)
			throw new InvalidDataException(
				"The section milestone render range is invalid.");
		TimeSpan cursor = value.TimelineStart;
		for (int index = 0; index < value.Chunks.Count; index++)
		{
			RoughCutRenderChunk chunk = value.Chunks[index];
			if (chunk.ChunkIndex != index + 1 ||
				(chunk.Start - cursor).Duration() > BoundaryTolerance ||
				chunk.Duration <= TimeSpan.Zero ||
				chunk.Duration > MaximumChunkDuration ||
				string.IsNullOrWhiteSpace(chunk.OutputRelativePath) ||
				Path.IsPathRooted(chunk.OutputRelativePath) ||
				chunk.OutputRelativePath.Split('/', '\\')
					.Any(segment => segment == "..") ||
				chunk.Sha256 == null ||
				chunk.Sha256.Length != 64 ||
				chunk.Sha256.Any(character => !Uri.IsHexDigit(character)))
				throw new InvalidDataException(
					"The section milestone render chunks are invalid.");
			cursor = chunk.Start + chunk.Duration;
		}
		if ((cursor - value.TimelineEnd).Duration() > BoundaryTolerance)
			throw new InvalidDataException(
				"The section milestone chunks do not cover the section.");
	}
}
