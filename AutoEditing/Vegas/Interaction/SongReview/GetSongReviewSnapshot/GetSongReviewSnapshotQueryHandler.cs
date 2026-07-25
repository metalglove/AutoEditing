using System;
using System.Collections.Generic;
using Core.Domain.Audio.SongAnalysis;
using Newtonsoft.Json;
using ScriptPortal.Vegas;
namespace Core.Scripts;
internal sealed class GetSongReviewSnapshotQueryHandler : IVegasCommandHandler
{
	public string CommandType => "GetSongReviewSnapshot";
	public string Execute(Vegas vegas, string payloadJson)
	{
		SongReviewSnapshot snapshot = new SongReviewSnapshot();
		foreach (Marker marker in (IEnumerable<Marker>)vegas.Project.Markers)
		{
			string[] parts = marker.Label?.Split('|');
			MusicEventType type;
			if (parts != null && parts.Length >= 4 && parts[0] == "AE" && parts[1] == "MUSIC" && Enum.TryParse(parts[3], true, out type))
				snapshot.Events.Add(new SongReviewEventSnapshot { Id = parts[2], TimeSeconds = Seconds(marker.Position), Type = type });
		}
		foreach (Region region in (IEnumerable<Region>)vegas.Project.Regions)
		{
			string[] parts = ((Marker)region).Label?.Split('|');
			MusicRegionType type;
			if (parts != null && parts.Length >= 4 && parts[0] == "AE" && parts[1] == "MUSIC_REGION" && Enum.TryParse(parts[3], true, out type))
			{
				double start = Seconds(((Marker)region).Position);
				snapshot.Regions.Add(new SongReviewRegionSnapshot { Id = parts[2], StartSeconds = start, EndSeconds = start + Seconds(region.Length), Type = type });
			}
		}
		return JsonConvert.SerializeObject(snapshot);
	}
	private static double Seconds(Timecode timecode) { return timecode.ToMilliseconds() / 1000.0; }
}
