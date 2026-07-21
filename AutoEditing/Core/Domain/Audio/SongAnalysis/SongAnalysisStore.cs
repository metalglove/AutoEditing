using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Core.Domain.Audio.SongAnalysis;

public sealed class SongAnalysisStore
{
	public string GetSidecarPath(string songPath)
	{
		if (string.IsNullOrWhiteSpace(songPath))
		{
			throw new ArgumentException("A song path is required.", nameof(songPath));
		}
		return songPath + ".autoediting.song-analysis.json";
	}

	public SongAnalysis Load(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		SongAnalysis analysis = JsonConvert.DeserializeObject<SongAnalysis>(File.ReadAllText(path), new StringEnumConverter());
		Validate(analysis);
		return analysis;
	}

	public void Save(string path, SongAnalysis analysis)
	{
		Validate(analysis);
		analysis.UpdatedUtc = DateTime.UtcNow;
		string directory = Path.GetDirectoryName(Path.GetFullPath(path));
		Directory.CreateDirectory(directory);
		string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			string json = JsonConvert.SerializeObject(analysis, Formatting.Indented, new StringEnumConverter());
			File.WriteAllText(temporaryPath, json);
			if (File.Exists(path))
			{
				File.Replace(temporaryPath, path, null);
			}
			else
			{
				File.Move(temporaryPath, path);
			}
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	private static void Validate(SongAnalysis analysis)
	{
		if (analysis == null)
		{
			throw new InvalidDataException("Song analysis is empty or invalid.");
		}
		if (analysis.SchemaVersion != SongAnalysis.CurrentSchemaVersion)
		{
			throw new NotSupportedException("Unsupported song-analysis schema version " + analysis.SchemaVersion + ".");
		}
		if (analysis.Song == null || string.IsNullOrWhiteSpace(analysis.Song.ContentFingerprint))
		{
			throw new InvalidDataException("Song analysis has no content fingerprint.");
		}
		if (string.IsNullOrWhiteSpace(analysis.Id))
		{
			throw new InvalidDataException("Song analysis has no stable ID.");
		}
		if (analysis.Song.DurationSeconds < 0.0)
		{
			throw new InvalidDataException("Song duration cannot be negative.");
		}
		HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (MusicEvent musicEvent in analysis.Events)
		{
			musicEvent.Editorial = musicEvent.Editorial ?? new EditorialMetadata();
			musicEvent.Editorial.AllowedUses = musicEvent.Editorial.AllowedUses ?? new List<EditorialUse>();
			musicEvent.Editorial.Assignments = musicEvent.Editorial.Assignments ?? new List<EditorialAssignment>();
			if (string.IsNullOrWhiteSpace(musicEvent.Id) || !ids.Add(musicEvent.Id))
			{
				throw new InvalidDataException("Music event IDs must be present and unique.");
			}
			if (musicEvent.TimeSeconds < 0.0 || musicEvent.TimeSeconds > analysis.Song.DurationSeconds + 0.001)
			{
				throw new InvalidDataException("Music event lies outside the song: " + musicEvent.Id);
			}
			IReadOnlyList<string> editorialErrors = EditorialMetadataValidator.Validate(musicEvent);
			if (editorialErrors.Count > 0) throw new InvalidDataException("Editorial assignment for event at " + musicEvent.TimeSeconds.ToString("0.000") + "s is invalid: " + string.Join(" ", editorialErrors));
		}
		foreach (MusicRegion region in analysis.Regions)
		{
			if (string.IsNullOrWhiteSpace(region.Id) || !ids.Add(region.Id))
			{
				throw new InvalidDataException("Music event and region IDs must be present and unique.");
			}
			if (region.StartSeconds < 0.0 || region.EndSeconds <= region.StartSeconds || region.EndSeconds > analysis.Song.DurationSeconds + 0.001)
			{
				throw new InvalidDataException("Music region has an invalid range: " + region.Id);
			}
		}
	}
}
