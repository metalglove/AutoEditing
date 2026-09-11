using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Scripts;

internal static class PreviewRenderProfileResolver
{
	public static PreviewRenderProfile Resolve(
		string profileId,
		IEnumerable<PreviewRendererCapability> capabilities)
	{
		if (!string.Equals(profileId, "review-1080p", StringComparison.Ordinal))
			throw new InvalidOperationException("Unknown preview render profile '" + profileId + "'.");
		List<Tuple<PreviewRendererCapability, PreviewTemplateCapability>> candidates =
			(capabilities ?? throw new ArgumentNullException(nameof(capabilities)))
			.SelectMany(renderer => renderer.Templates.Select(template => Tuple.Create(renderer, template)))
			.Where(item => item.Item2.IsValid &&
				item.Item2.VideoStreamCount > 0 &&
				item.Item2.FileExtensions.Any(extension =>
					string.Equals(Normalize(extension), ".mp4", StringComparison.OrdinalIgnoreCase)))
			.OrderByDescending(item => Contains1080(item.Item2.DisplayName))
			.ThenBy(item => item.Item1.ClassId, StringComparer.Ordinal)
			.ThenBy(item => item.Item2.TemplateId, StringComparer.Ordinal)
			.ToList();
		if (candidates.Count == 0)
			throw new InvalidOperationException("No valid MP4 video render template is available.");
		Tuple<PreviewRendererCapability, PreviewTemplateCapability> selected = candidates[0];
		return new PreviewRenderProfile(
			profileId,
			selected.Item1.ClassId,
			selected.Item2.TemplateId,
			new[] { ".mp4" },
			20);
	}

	private static bool Contains1080(string value) =>
		(value ?? "").IndexOf("1080", StringComparison.OrdinalIgnoreCase) >= 0;

	private static string Normalize(string value)
	{
		string normalized = (value ?? "").Trim();
		if (normalized.StartsWith("*", StringComparison.Ordinal)) normalized = normalized.Substring(1);
		return normalized.StartsWith(".", StringComparison.Ordinal) ? normalized : "." + normalized;
	}
}
