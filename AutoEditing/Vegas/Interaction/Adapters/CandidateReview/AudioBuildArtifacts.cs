using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class AudioBuildArtifacts
{
	public AudioBuildArtifacts(IReadOnlyList<AudioTrack> tracks, IReadOnlyList<AudioEvent> events)
	{
		if (tracks == null) throw new ArgumentNullException(nameof(tracks));
		if (events == null) throw new ArgumentNullException(nameof(events));
		Tracks = new ReadOnlyCollection<AudioTrack>(tracks.ToList());
		Events = new ReadOnlyCollection<AudioEvent>(events.ToList());
	}

	public IReadOnlyList<AudioTrack> Tracks { get; }
	public IReadOnlyList<AudioEvent> Events { get; }
}
