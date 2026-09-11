using System.Collections.Generic;

namespace Core.Scripts;

internal sealed class ReviewClipSnapshot
{
	public bool Exists { get; set; }
	public double RegionStartSeconds { get; set; }
	public double RegionEndSeconds { get; set; }
	public double CursorSeconds { get; set; }
	public List<ReviewMarkerSnapshot> Markers { get; set; } = new List<ReviewMarkerSnapshot>();
}
