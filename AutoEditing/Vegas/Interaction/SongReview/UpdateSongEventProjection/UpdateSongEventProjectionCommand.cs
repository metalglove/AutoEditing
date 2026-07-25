using System.Collections.Generic;
using Core.Domain.Audio.SongAnalysis;

namespace Core.Scripts;

internal sealed class UpdateSongEventProjectionCommand : IVegasCommand
{
	public string CommandType => "UpdateSongEventProjection";

	public SongAnalysis Analysis { get; set; }

	public List<string> EventIds { get; set; } = new List<string>();
}
