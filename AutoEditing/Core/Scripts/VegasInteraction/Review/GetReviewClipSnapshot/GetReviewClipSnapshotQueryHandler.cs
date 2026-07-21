using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using ScriptPortal.Vegas;
namespace Core.Scripts;
internal sealed class GetReviewClipSnapshotQueryHandler : IVegasCommandHandler
{
	public string CommandType => "GetReviewClipSnapshot";
	public string Execute(Vegas vegas, string payloadJson)
	{
		GetReviewClipSnapshotQuery command = JsonConvert.DeserializeObject<GetReviewClipSnapshotQuery>(payloadJson);
		if (command == null) throw new InvalidOperationException("Review snapshot query is empty.");
		string prefix = "AE|CLIP|" + command.ClipIndex + "|";
		Region region = ((IEnumerable<Region>)vegas.Project.Regions).FirstOrDefault((Region value) => ((Marker)value).Label != null && ((Marker)value).Label.StartsWith(prefix, StringComparison.Ordinal));
		ReviewClipSnapshot snapshot = new ReviewClipSnapshot { Exists = region != null };
		if (region != null)
		{
			snapshot.RegionStartSeconds = Seconds(((Marker)region).Position);
			snapshot.RegionEndSeconds = snapshot.RegionStartSeconds + Seconds(region.Length);
			snapshot.Markers = ((IEnumerable<Marker>)vegas.Project.Markers).Where((Marker marker) => MarkerIndex(marker.Label) == command.ClipIndex).Select((Marker marker) => new ReviewMarkerSnapshot { TimelineSeconds = Seconds(marker.Position), Label = marker.Label }).OrderBy((ReviewMarkerSnapshot marker) => marker.TimelineSeconds).ToList();
		}
		snapshot.CursorSeconds = Seconds(vegas.Transport.CursorPosition);
		return JsonConvert.SerializeObject(snapshot);
	}
	private static int MarkerIndex(string label) { string[] parts = (label ?? string.Empty).Split('|'); int index; return parts.Length >= 3 && parts[0] == "AE" && int.TryParse(parts[2], out index) ? index : -1; }
	private static double Seconds(Timecode value) { return value.ToMilliseconds() / 1000.0; }
}
