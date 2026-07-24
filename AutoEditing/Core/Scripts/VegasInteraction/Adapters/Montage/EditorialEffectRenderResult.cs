namespace Core.Scripts;

internal sealed class EditorialEffectRenderResult
{
	private EditorialEffectRenderResult(bool rendered, string reason)
	{
		Rendered = rendered;
		Reason = reason;
	}

	public bool Rendered { get; }
	public string Reason { get; }

	public static EditorialEffectRenderResult Success(string description)
	{
		return new EditorialEffectRenderResult(true, description);
	}

	public static EditorialEffectRenderResult Unsupported(string reason)
	{
		return new EditorialEffectRenderResult(false, reason);
	}
}
