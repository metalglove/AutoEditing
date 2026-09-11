using System;
using System.Collections.Generic;
using System.IO;

namespace Core.Scripts;

internal static class PreviewRenderPlanFactory
{
	private const double Epsilon = 0.000001;

	public static PreviewRenderPlan Create(
		string sessionArtifactRoot,
		string outputRelativePath,
		double candidateStartSeconds,
		double candidateEndSeconds,
		double previewStartSeconds,
		double previewDurationSeconds,
		PreviewRenderProfile profile,
		IEnumerable<PreviewRendererCapability> capabilities)
	{
		if (profile == null) throw new ArgumentNullException(nameof(profile));
		if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
		if (!IsFinite(candidateStartSeconds) || !IsFinite(candidateEndSeconds) ||
			candidateStartSeconds < 0 || candidateEndSeconds <= candidateStartSeconds)
			throw new ArgumentOutOfRangeException(nameof(candidateEndSeconds), "Candidate bounds are invalid.");
		if (!IsFinite(previewStartSeconds) || !IsFinite(previewDurationSeconds) ||
			previewDurationSeconds <= 0)
			throw new ArgumentOutOfRangeException(nameof(previewDurationSeconds), "Preview range is invalid.");
		if (previewDurationSeconds > profile.MaximumDurationSeconds + Epsilon)
			throw new InvalidOperationException("The preview exceeds the configured maximum duration.");
		double previewEnd = previewStartSeconds + previewDurationSeconds;
		if (previewStartSeconds < candidateStartSeconds - Epsilon ||
			previewEnd > candidateEndSeconds + Epsilon)
			throw new InvalidOperationException("The preview range is outside the candidate workspace bounds.");

		string outputPath = ResolveContainedOutput(sessionArtifactRoot, outputRelativePath);
		string extension = Path.GetExtension(outputPath);
		PreviewRendererSelection selection =
			PreviewRendererSelector.Select(profile, capabilities, extension);
		return new PreviewRenderPlan(selection, outputPath, previewStartSeconds, previewDurationSeconds);
	}

	public static string ResolveContainedOutput(string sessionArtifactRoot, string outputRelativePath)
	{
		if (string.IsNullOrWhiteSpace(sessionArtifactRoot))
			throw new ArgumentException("A session artifact root is required.", nameof(sessionArtifactRoot));
		if (string.IsNullOrWhiteSpace(outputRelativePath) || Path.IsPathRooted(outputRelativePath))
			throw new InvalidOperationException("Preview output must be a relative session artifact path.");
		string root = Path.GetFullPath(sessionArtifactRoot)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string output = Path.GetFullPath(Path.Combine(root, outputRelativePath));
		string prefix = root + Path.DirectorySeparatorChar;
		if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Preview output escapes the session artifact root.");
		if (string.IsNullOrWhiteSpace(Path.GetFileName(output)))
			throw new InvalidOperationException("Preview output must identify a file.");
		return output;
	}

	private static bool IsFinite(double value) =>
		!double.IsNaN(value) && !double.IsInfinity(value);
}
