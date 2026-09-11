using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain.Audio.SongAnalysis;

public static class EditorialMetadataValidator
{
	public static IReadOnlyList<string> Validate(MusicEvent musicEvent)
	{
		List<string> errors = new List<string>();
		EditorialMetadata editorial = musicEvent.Editorial ?? new EditorialMetadata();
		List<EditorialAssignment> assignments = editorial.Assignments ?? new List<EditorialAssignment>();
		if (editorial.Intensity.HasValue && (double.IsNaN(editorial.Intensity.Value) || double.IsInfinity(editorial.Intensity.Value) || editorial.Intensity.Value < 0.0 || editorial.Intensity.Value > 1.0))
		{
			errors.Add("Intensity must be between 0% and 100%.");
		}
		if (editorial.TimingOffsetSeconds.HasValue && (double.IsNaN(editorial.TimingOffsetSeconds.Value) || double.IsInfinity(editorial.TimingOffsetSeconds.Value) || Math.Abs(editorial.TimingOffsetSeconds.Value) > 2.0))
		{
			errors.Add("Timing offset must be between -2.0 and +2.0 seconds.");
		}
		foreach (IGrouping<string, EditorialAssignment> conflict in assignments.Where((EditorialAssignment item) => item != null && item.Use != EditorialUse.None).GroupBy((EditorialAssignment item) => Category(item.Use)).Where((IGrouping<string, EditorialAssignment> group) => group.Count() > 1))
		{
			errors.Add("Choose at most one " + conflict.Key + " assignment; currently selected: " + string.Join(", ", conflict.Select((EditorialAssignment item) => item.Use)) + ".");
		}
		if (assignments.Any((EditorialAssignment item) => item?.Use == EditorialUse.IntentionallyUnused) && assignments.Any((EditorialAssignment item) => item != null && item.Use != EditorialUse.None && item.Use != EditorialUse.IntentionallyUnused))
		{
			errors.Add("Intentionally unused cannot be combined with another editorial assignment.");
		}
		if (editorial.AllowedUses != null && editorial.AllowedUses.Count > 0)
		{
			foreach (EditorialAssignment assignment in assignments.Where((EditorialAssignment item) => item != null && item.Use != EditorialUse.None && !editorial.AllowedUses.Contains(item.Use)))
			{
				errors.Add(assignment.Use + " is selected but is not in this event's allowed uses.");
			}
		}
		return errors;
	}

	public static string Category(EditorialUse use)
	{
		if (use == EditorialUse.Flash || use == EditorialUse.ScreenPump || use == EditorialUse.Shake) return "visual accent";
		if (use == EditorialUse.SpeedChange) return "timing effect";
		return "structural anchor";
	}
}
