using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Core.Domain.Editing;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class MontageBuildArtifacts
{
	public MontageBuildArtifacts(
		MontageBuildContext context,
		Dictionary<ClipPlacement, VideoEvent> videoEvents,
		IReadOnlyList<Track> createdTracks,
		IReadOnlyList<TrackEvent> createdEvents)
	{
		Context = context ?? throw new ArgumentNullException(nameof(context));
		if (videoEvents == null) throw new ArgumentNullException(nameof(videoEvents));
		if (createdTracks == null) throw new ArgumentNullException(nameof(createdTracks));
		if (createdEvents == null) throw new ArgumentNullException(nameof(createdEvents));
		VideoEvents = new Dictionary<ClipPlacement, VideoEvent>(videoEvents);
		CreatedTracks = new ReadOnlyCollection<Track>(createdTracks.ToList());
		CreatedEvents = new ReadOnlyCollection<TrackEvent>(createdEvents.ToList());
	}

	public MontageBuildContext Context { get; }
	public Dictionary<ClipPlacement, VideoEvent> VideoEvents { get; }
	public IReadOnlyList<Track> CreatedTracks { get; }
	public IReadOnlyList<TrackEvent> CreatedEvents { get; }
}
