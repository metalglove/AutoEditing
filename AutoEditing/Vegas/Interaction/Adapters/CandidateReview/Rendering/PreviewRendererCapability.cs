using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Scripts;

internal sealed class PreviewRendererCapability
{
	public PreviewRendererCapability(
		string classId,
		string displayName,
		IEnumerable<PreviewTemplateCapability> templates)
	{
		if (!Guid.TryParse(classId, out Guid parsed))
			throw new ArgumentException("Renderer capability IDs must be GUIDs.", nameof(classId));
		ClassId = parsed.ToString("D");
		DisplayName = displayName ?? "";
		Templates = (templates ?? throw new ArgumentNullException(nameof(templates))).ToList().AsReadOnly();
	}

	public string ClassId { get; }
	public string DisplayName { get; }
	public IReadOnlyList<PreviewTemplateCapability> Templates { get; }
}

internal sealed class PreviewTemplateCapability
{
	public PreviewTemplateCapability(
		string templateId,
		string displayName,
		bool isValid,
		int videoStreamCount,
		IEnumerable<string> fileExtensions)
	{
		if (!Guid.TryParse(templateId, out Guid parsed))
			throw new ArgumentException("Render template capability IDs must be GUIDs.", nameof(templateId));
		TemplateId = parsed.ToString("D");
		DisplayName = displayName ?? "";
		IsValid = isValid;
		VideoStreamCount = videoStreamCount;
		FileExtensions = (fileExtensions ?? Array.Empty<string>()).ToList().AsReadOnly();
	}

	public string TemplateId { get; }
	public string DisplayName { get; }
	public bool IsValid { get; }
	public int VideoStreamCount { get; }
	public IReadOnlyList<string> FileExtensions { get; }
}
