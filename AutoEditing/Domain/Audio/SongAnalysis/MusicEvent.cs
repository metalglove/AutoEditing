namespace Core.Domain.Audio.SongAnalysis;

public sealed class MusicEvent
{
	public string Id { get; set; }

	public double TimeSeconds { get; set; }

	public MusicEventType Type { get; set; }

	public double? Strength { get; set; }

	public double? Confidence { get; set; }

	public MusicAnalysisOrigin Origin { get; set; }

	public MusicAnalysisReviewState ReviewState { get; set; }

	public double? DetectedTimeSeconds { get; set; }

	public MusicEventType? DetectedType { get; set; }

	public EditorialMetadata Editorial { get; set; } = new EditorialMetadata();
}
