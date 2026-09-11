using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Scripts;

internal sealed class PreviewRendererSelection
{
	public PreviewRendererSelection(
		PreviewRenderProfile profile,
		PreviewRendererCapability renderer,
		PreviewTemplateCapability template)
	{
		Profile = profile;
		Renderer = renderer;
		Template = template;
	}

	public PreviewRenderProfile Profile { get; }
	public PreviewRendererCapability Renderer { get; }
	public PreviewTemplateCapability Template { get; }
}

internal static class PreviewRendererSelector
{
	public static PreviewRendererSelection Select(
		PreviewRenderProfile profile,
		IEnumerable<PreviewRendererCapability> capabilities,
		string outputExtension)
	{
		if (profile == null) throw new ArgumentNullException(nameof(profile));
		if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
		if (!profile.AllowsExtension(outputExtension))
			throw new InvalidOperationException(
				"Output extension '" + outputExtension + "' is not allowed by render profile '" + profile.Id + "'.");

		List<PreviewRendererCapability> renderers = capabilities
			.Where(item => string.Equals(item.ClassId, profile.RendererClassId, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (renderers.Count != 1)
			throw new InvalidOperationException(
				renderers.Count == 0
					? "The configured preview renderer is unavailable."
					: "The configured preview renderer is ambiguous.");

		List<PreviewTemplateCapability> templates = renderers[0].Templates
			.Where(item => string.Equals(item.TemplateId, profile.TemplateId, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (templates.Count != 1)
			throw new InvalidOperationException(
				templates.Count == 0
					? "The configured preview render template is unavailable."
					: "The configured preview render template is ambiguous.");
		PreviewTemplateCapability template = templates[0];
		if (!template.IsValid)
			throw new InvalidOperationException("The configured preview render template is not valid in this VEGAS host.");
		if (template.VideoStreamCount < 1)
			throw new InvalidOperationException("The configured preview render template has no video stream.");
		if (!template.FileExtensions.Any(item =>
			string.Equals(Normalize(item), Normalize(outputExtension), StringComparison.OrdinalIgnoreCase)))
		{
			throw new InvalidOperationException(
				"The configured preview render template does not support the requested output extension.");
		}
		return new PreviewRendererSelection(profile, renderers[0], template);
	}

	private static string Normalize(string value)
	{
		string normalized = (value ?? "").Trim();
		if (normalized.StartsWith("*", StringComparison.Ordinal))
			normalized = normalized.Substring(1);
		return string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith(".", StringComparison.Ordinal)
			? normalized
			: "." + normalized;
	}
}
