namespace Core.Domain.Audio.SongAnalysis;

public sealed class MusicRegion
{
	public string Id { get; set; }

	public double StartSeconds { get; set; }

	public double EndSeconds { get; set; }

	public MusicRegionType Type { get; set; }

	public double? Confidence { get; set; }

	public MusicAnalysisOrigin Origin { get; set; }

	public MusicAnalysisReviewState ReviewState { get; set; }

	public double? DetectedStartSeconds { get; set; }

	public double? DetectedEndSeconds { get; set; }

	public MusicRegionType? DetectedType { get; set; }

	public EditorialMetadata Editorial { get; set; } = new EditorialMetadata();
}
