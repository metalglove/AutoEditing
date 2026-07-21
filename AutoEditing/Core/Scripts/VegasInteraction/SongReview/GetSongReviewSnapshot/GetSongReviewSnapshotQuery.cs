namespace Core.Scripts;
internal sealed class GetSongReviewSnapshotQuery : IVegasQuery<SongReviewSnapshot>
{
	public string CommandType => "GetSongReviewSnapshot";
}
