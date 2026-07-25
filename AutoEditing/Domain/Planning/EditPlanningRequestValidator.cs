using System;
using System.Collections.Generic;

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

		foreach (Core.Domain.Clip.Clip clip in request.Clips)
		{
			if (clip == null || string.IsNullOrWhiteSpace(clip.FilePath))
				throw new InvalidOperationException("An edit planning request clip has no identity or media path.");
			if (!Finite(clip.DurationSeconds) || clip.DurationSeconds <= 0.0)
				throw new InvalidOperationException("An edit planning request clip has an invalid duration: " + clip.FilePath);
		}
	}

	private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
