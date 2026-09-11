using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Scripts;

internal sealed class PreviewRenderProfile
{
	private static readonly Regex IdPattern = new Regex(
		"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
		RegexOptions.CultureInvariant);

	public PreviewRenderProfile(
		string id,
		string rendererClassId,
		string templateId,
		IEnumerable<string> allowedExtensions,
		double maximumDurationSeconds)
	{
		if (string.IsNullOrWhiteSpace(id) || !IdPattern.IsMatch(id))
			throw new ArgumentException("Render profile IDs must be stable safe identifiers.", nameof(id));
		if (!Guid.TryParse(rendererClassId, out Guid rendererGuid))
			throw new ArgumentException("RendererClassId must be a GUID.", nameof(rendererClassId));
		if (!Guid.TryParse(templateId, out Guid templateGuid))
			throw new ArgumentException("TemplateId must be a GUID.", nameof(templateId));
		if (allowedExtensions == null) throw new ArgumentNullException(nameof(allowedExtensions));
		string[] extensions = allowedExtensions
			.Select(NormalizeExtension)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (extensions.Length == 0)
			throw new ArgumentException("At least one output extension is required.", nameof(allowedExtensions));
		if (!IsFinitePositive(maximumDurationSeconds))
			throw new ArgumentOutOfRangeException(nameof(maximumDurationSeconds));

		Id = id;
		RendererClassId = rendererGuid.ToString("D");
		TemplateId = templateGuid.ToString("D");
		AllowedExtensions = extensions;
		MaximumDurationSeconds = maximumDurationSeconds;
	}

	public string Id { get; }
	public string RendererClassId { get; }
	public string TemplateId { get; }
	public IReadOnlyList<string> AllowedExtensions { get; }
	public double MaximumDurationSeconds { get; }

	public bool AllowsExtension(string extension) =>
		AllowedExtensions.Contains(NormalizeExtension(extension), StringComparer.OrdinalIgnoreCase);

	private static string NormalizeExtension(string extension)
	{
		if (string.IsNullOrWhiteSpace(extension))
			throw new ArgumentException("Output extensions cannot be empty.", nameof(extension));
		string normalized = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
		if (normalized.IndexOfAny(new[] { '/', '\\', ':', '*' }) >= 0)
			throw new ArgumentException("Output extensions cannot contain path characters.", nameof(extension));
		return normalized.ToLowerInvariant();
	}

	private static bool IsFinitePositive(double value) =>
		!double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
}
