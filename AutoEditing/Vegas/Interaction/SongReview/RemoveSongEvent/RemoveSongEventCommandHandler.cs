using System;
using System.Collections.Generic;
using System.Linq;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class RemoveSongEventCommandHandler : VegasCommandHandler<RemoveSongEventCommand>
{
	public override string CommandType => "RemoveSongEvent";

	protected override void Execute(Vegas vegas, RemoveSongEventCommand command)
	{
		if (string.IsNullOrWhiteSpace(command.EventId)) throw new InvalidOperationException("Song event ID is required.");
		string prefix = "AE|MUSIC|" + command.EventId + "|";
		foreach (Marker marker in ((IEnumerable<Marker>)vegas.Project.Markers).Where((Marker item) => item.Label != null && item.Label.StartsWith(prefix, StringComparison.Ordinal)).ToList())
		{
			((BaseList<Marker>)(object)vegas.Project.Markers).Remove(marker);
		}
		vegas.UpdateUI();
	}
}
