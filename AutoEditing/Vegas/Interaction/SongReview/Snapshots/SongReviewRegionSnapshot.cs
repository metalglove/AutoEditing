using Core.Domain.Audio.SongAnalysis;
namespace Core.Scripts;
internal sealed class SongReviewRegionSnapshot
{
	public string Id { get; set; }
	public double StartSeconds { get; set; }
	public double EndSeconds { get; set; }
	public MusicRegionType Type { get; set; }
}
