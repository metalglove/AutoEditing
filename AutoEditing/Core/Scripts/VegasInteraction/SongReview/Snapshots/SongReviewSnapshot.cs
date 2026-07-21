using System.Collections.Generic;
namespace Core.Scripts;
internal sealed class SongReviewSnapshot
{
	public List<SongReviewEventSnapshot> Events { get; set; } = new List<SongReviewEventSnapshot>();
	public List<SongReviewRegionSnapshot> Regions { get; set; } = new List<SongReviewRegionSnapshot>();
}
