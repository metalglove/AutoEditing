using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoEditing.Iteration.Contracts.Automation;

namespace AutoEditing.Iteration.Contracts.Assembly;

public sealed class RoughCutRenderChunk
{
	public int ChunkIndex { get; set; }
	public TimeSpan Start { get; set; }
	public TimeSpan Duration { get; set; }
	public string OutputRelativePath { get; set; } = "";
	public string Sha256 { get; set; } = "";
}

/// <summary>
/// Complete synchronization-pass render evidence. The render is divided into
/// contiguous bounded chunks because checkpoint preview rendering has a strict
/// twenty-second safety limit. Collectively the chunks cover the full accepted
/// rough cut without gaps or overlap.
/// </summary>
public sealed class RoughCutRenderManifest
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string SessionId { get; set; } = "";
	public string RenderId { get; set; } = "";
	public CandidateWorkspaceId Workspace { get; set; }
	public string PlanSha256 { get; set; } = "";
	public string RenderProfileId { get; set; } = "";
	public TimeSpan TimelineStart { get; set; }
	public TimeSpan TimelineEnd { get; set; }
	public DateTimeOffset CompletedUtc { get; set; }
	public IList<RoughCutRenderChunk> Chunks { get; set; } =
		new List<RoughCutRenderChunk>();
}

public static class RoughCutRenderContractValidator
{
	private static readonly TimeSpan MaximumChunkDuration = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan BoundaryTolerance = TimeSpan.FromMilliseconds(2);

	public static void Validate(RoughCutRenderManifest manifest)
	{
		if (manifest == null) throw new ArgumentNullException(nameof(manifest));
		if (manifest.SchemaVersion != RoughCutRenderManifest.CurrentSchemaVersion)
			throw new InvalidDataException(
				$"Unsupported rough-cut render schema version {manifest.SchemaVersion}.");
		Text(manifest.SessionId, "Render session ID");
		Text(manifest.RenderId, "Render ID");
		manifest.Workspace?.Validate();
		if (manifest.Workspace == null)
			throw new InvalidDataException("Render workspace is required.");
		Hash(manifest.PlanSha256, "Render plan hash");
		Text(manifest.RenderProfileId, "Render profile");
		if (manifest.TimelineStart < TimeSpan.Zero ||
			manifest.TimelineEnd <= manifest.TimelineStart)
			throw new InvalidDataException("Rough-cut render bounds are invalid.");
		if (manifest.CompletedUtc == default)
			throw new InvalidDataException("Rough-cut render completion time is required.");
		if (manifest.Chunks == null || manifest.Chunks.Count == 0)
			throw new InvalidDataException("A rough-cut render requires at least one chunk.");

		TimeSpan expected = manifest.TimelineStart;
		for (int index = 0; index < manifest.Chunks.Count; index++)
		{
			RoughCutRenderChunk chunk = manifest.Chunks[index];
			if (chunk.ChunkIndex != index + 1)
				throw new InvalidDataException(
					"Rough-cut render chunks must be contiguous and one-based.");
			if ((chunk.Start - expected).Duration() > BoundaryTolerance)
				throw new InvalidDataException(
					"Rough-cut render chunks contain a gap or overlap.");
			if (chunk.Duration <= TimeSpan.Zero ||
				chunk.Duration > MaximumChunkDuration)
				throw new InvalidDataException(
					"Rough-cut render chunks must be between zero and twenty seconds.");
			RelativePath(chunk.OutputRelativePath);
			Hash(chunk.Sha256, "Render chunk hash");
			expected = chunk.Start + chunk.Duration;
		}
		if ((expected - manifest.TimelineEnd).Duration() > BoundaryTolerance)
			throw new InvalidDataException(
				"Rough-cut render chunks do not cover the complete synchronization pass.");
	}

	private static void Text(string value, string name)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new InvalidDataException(name + " is required.");
	}

	private static void Hash(string value, string name)
	{
		if (value == null || value.Length != 64 ||
			value.Any(character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException(name + " is not a SHA-256 value.");
	}

	private static void RelativePath(string value)
	{
		Text(value, "Render chunk path");
		if (Path.IsPathRooted(value) ||
			value.Split('/', '\\').Any(segment => segment == ".."))
			throw new InvalidDataException("Render chunk paths must be safe and relative.");
	}
}
