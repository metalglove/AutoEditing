using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Domain.Clip;

public class ClipParser
{
	private static readonly Regex NotesRegex = new Regex("\\(([^)]*)\\)", RegexOptions.Compiled);

	public List<Clip> ParseAllClips(string folderPath)
	{
		List<Clip> list = new List<Clip>();
		string[] files = Directory.GetFiles(folderPath, "*.mp4");
		foreach (string filePath in files)
		{
			Clip clip = ParseClip(filePath);
			if (clip != null)
			{
				list.Add(clip);
			}
		}
		return list;
	}

	public Clip ParseClip(string filePath)
	{
		string text = Path.GetFileNameWithoutExtension(filePath);
		Clip clip = new Clip
		{
			FilePath = filePath
		};
		if (text.StartsWith("[OPENER]", StringComparison.OrdinalIgnoreCase))
		{
			clip.IsOpener = true;
			text = text.Substring("[OPENER]".Length);
		}
		else if (text.StartsWith("[CLOSER]", StringComparison.OrdinalIgnoreCase))
		{
			clip.IsCloser = true;
			text = text.Substring("[CLOSER]".Length);
		}
		string[] array = (from p in text.Split('-')
			select p.Trim()).ToArray();
		if (array.Length < 4 || array.Take(4).Any(string.IsNullOrWhiteSpace))
		{
			return null;
		}
		clip.PlayerName = array[0];
		clip.Game = array[1];
		clip.Map = array[2];
		clip.SequenceNumber = 1;
		if (array.Length >= 5)
		{
			ParseLegacyParts(clip, array);
		}
		else
		{
			ParseDetails(clip, array[3]);
		}
		return string.IsNullOrWhiteSpace(clip.Gun) ? null : clip;
	}

	private static void ParseLegacyParts(Clip clip, string[] parts)
	{
		clip.Gun = parts[3];
		clip.ClipType = parts[4];
		if (parts.Length > 5 && int.TryParse(parts[5], out var result) && result >= 1)
		{
			clip.SequenceNumber = result;
		}
	}

	private static void ParseDetails(Clip clip, string details)
	{
		Match match = NotesRegex.Match(details);
		if (match.Success)
		{
			clip.Notes = match.Groups[1].Value.Trim();
			details = NotesRegex.Replace(details, " ");
		}
		List<string> list = details.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
		if (list.Count == 0)
		{
			return;
		}
		clip.Gun = list[0];
		list.RemoveAt(0);
		List<string> list2 = new List<string>();
		foreach (string item in list)
		{
			if (IsSequenceNumber(item, out var sequenceNumber))
			{
				clip.SequenceNumber = sequenceNumber;
			}
			else
			{
				list2.Add(item);
			}
		}
		clip.ClipType = ((list2.Count > 0) ? string.Join(" ", list2) : "Clip");
	}

	private static bool IsSequenceNumber(string token, out int sequenceNumber)
	{
		sequenceNumber = 0;
		if (!token.All(char.IsDigit))
		{
			return false;
		}
		return int.TryParse(token, out sequenceNumber) && sequenceNumber >= 1;
	}
}
