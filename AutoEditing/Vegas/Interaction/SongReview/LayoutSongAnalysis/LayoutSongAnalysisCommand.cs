using Core.Domain.Audio.SongAnalysis;

namespace Core.Scripts;

internal sealed class LayoutSongAnalysisCommand : IVegasCommand
{
	public string CommandType => "LayoutSongAnalysis";

	public string SongPath { get; set; }

	public SongAnalysis Analysis { get; set; }
}
