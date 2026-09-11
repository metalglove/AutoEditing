using System;
using System.Collections.Generic;
using Core.Domain.Editing;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class TimelineBuildArtifacts
{
	public TimelineBuildArtifacts(VideoTrack videoTrack, Dictionary<ClipPlacement, VideoEvent> videoEvents)
	{
		VideoTrack = videoTrack ?? throw new ArgumentNullException(nameof(videoTrack));
		VideoEvents = videoEvents ?? throw new ArgumentNullException(nameof(videoEvents));
	}

	public VideoTrack VideoTrack { get; }
	public Dictionary<ClipPlacement, VideoEvent> VideoEvents { get; }
}
