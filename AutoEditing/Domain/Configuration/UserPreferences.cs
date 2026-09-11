using System.Collections.Generic;

namespace Core.Domain;

public sealed class UserPreferences
{
	public bool HasSeenOnboarding { get; set; }
	public List<string> KnownClipDirectories { get; set; } = new List<string>();
}
