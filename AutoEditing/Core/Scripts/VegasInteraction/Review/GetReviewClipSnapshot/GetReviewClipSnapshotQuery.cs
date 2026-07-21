namespace Core.Scripts;
internal sealed class GetReviewClipSnapshotQuery : IVegasQuery<ReviewClipSnapshot>
{
	public string CommandType => "GetReviewClipSnapshot";
	public int ClipIndex { get; set; }
}
