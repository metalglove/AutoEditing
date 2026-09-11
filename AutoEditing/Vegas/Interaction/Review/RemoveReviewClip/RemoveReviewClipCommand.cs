namespace Core.Scripts;
internal sealed class RemoveReviewClipCommand : IVegasCommand
{
	public string CommandType => "RemoveReviewClip";
	public int ClipIndex { get; set; }
}
