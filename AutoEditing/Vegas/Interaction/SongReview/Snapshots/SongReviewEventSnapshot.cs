using Core.Domain.Audio.SongAnalysis;
namespace Core.Scripts;
internal sealed class SongReviewEventSnapshot
{
	public string Id { get; set; }
	public double TimeSeconds { get; set; }
	public MusicEventType Type { get; set; }
}
