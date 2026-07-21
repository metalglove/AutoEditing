using System.Collections.Generic;

namespace Core.Scripts;

public sealed class ClipDrawerRow
{
	public bool IsSelected { get; set; }
	public bool FileExists { get; set; }
	public string FilePath { get; set; }
	public string Player { get; set; }
	public string Game { get; set; }
	public string Map { get; set; }
	public string Guns { get; set; }
	public bool IsSwap { get; set; }
	public int SyncPointCount { get; set; }
	public string LeadTimes { get; set; }
	public bool IsReady { get; set; }
}
