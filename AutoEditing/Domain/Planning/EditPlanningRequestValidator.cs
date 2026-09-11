using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain.Planning;

public static class EditPlanningRequestValidator
{
	public static void ValidateAndNormalize(EditPlanningRequest request)
	{
		if (request == null) throw new InvalidOperationException("The edit planning request is empty.");
		if (request.SchemaVersion != EditPlanningRequest.CurrentSchemaVersion)
			throw new NotSupportedException("Unsupported edit planning request schema version " + request.SchemaVersion + ".");
		if (string.IsNullOrWhiteSpace(request.RequestId))
			throw new InvalidOperationException("The edit planning request has no request ID.");
		if (string.IsNullOrWhiteSpace(request.SongPath))
			throw new InvalidOperationException("The edit planning request has no song path.");

		request.Clips = request.Clips ?? new List<Core.Domain.Clip.Clip>();
		request.StyleProfileIds = request.StyleProfileIds ?? new List<string>();
		request.EffectOptions = request.EffectOptions ?? new Core.Domain.Editing.EffectSelectionOptions();
		request.EffectOptions.Validate();
		if (request.Clips.Count == 0)
			throw new InvalidOperationException("The edit planning request contains no clips.");
		if (request.SongAnalysis != null)
			ValidateSongAnalysis(request.SongAnalysis);

		foreach (Core.Domain.Clip.Clip clip in request.Clips)
		{
			if (clip == null || string.IsNullOrWhiteSpace(clip.FilePath))
				throw new InvalidOperationException("An edit planning request clip has no identity or media path.");
			if (!Finite(clip.DurationSeconds) || clip.DurationSeconds <= 0.0)
				throw new InvalidOperationException("An edit planning request clip has an invalid duration: " + clip.FilePath);
		}
	}

	private static void ValidateSongAnalysis(Core.Domain.Editing.MontageSongPlanningInput song)
	{
		if (string.IsNullOrWhiteSpace(song.SongFingerprint))
			throw new InvalidOperationException("The planning song analysis has no song fingerprint.");
		if (!Finite(song.SongDurationSeconds) || song.SongDurationSeconds <= 0.0)
			throw new InvalidOperationException("The planning song analysis has an invalid duration.");
		song.Regions = song.Regions ?? new List<Core.Domain.Editing.MontageSongPlanningRegion>();
		song.Events = song.Events ?? new List<Core.Domain.Editing.MontageSongPlanningEvent>();
		song.EventTimelineColumns = song.EventTimelineColumns ?? new List<string>();
		song.EventTimeline = song.EventTimeline ?? new List<List<object>>();
		song.Diagnostics = song.Diagnostics ?? new List<Core.Domain.Editing.MontageSongPlanningDiagnostic>();
		if (song.HasErrors)
			throw new InvalidOperationException("The planning song analysis contains errors.");
		if (song.Regions.Count == 0)
			throw new InvalidOperationException("The planning song analysis contains no reviewed regions.");
		if (song.Events.Count == 0)
			throw new InvalidOperationException("The planning song analysis contains no musical events.");
		string[] requiredColumns = { "timeSeconds", "type", "strength", "confidence", "reviewState" };
		if (!song.EventTimelineColumns.SequenceEqual(requiredColumns))
			throw new InvalidOperationException("The planning song event timeline has an unsupported column layout.");
		if (song.EventTimeline.Count == 0 || song.EventTimeline.Any(row => row == null || row.Count != requiredColumns.Length))
			throw new InvalidOperationException("The planning song event timeline is empty or malformed.");
	}

	private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
