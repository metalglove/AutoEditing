using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScriptPortal.Vegas;
using Core.Domain.Clip;

namespace Core.Scripts;

internal sealed class ClipValidator
{
	public bool Validate(Clip clip, Vegas vegas)
	{
		try
		{
			Media val = vegas.Project.MediaPool.AddMedia(clip.FilePath);
			if (val == (Media)null)
			{
				return false;
			}
			VideoStream val2 = ((IEnumerable)val.Streams).OfType<VideoStream>().FirstOrDefault();
			if (val2 == (VideoStream)null || val2.FrameRate < 60.0)
			{
				return false;
			}
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public string[] GetValidationErrors(Clip clip)
	{
		List<string> list = new List<string>();
		if (string.IsNullOrEmpty(clip.FilePath))
		{
			list.Add("File path is empty");
		}
		if (!File.Exists(clip.FilePath))
		{
			list.Add("File does not exist");
		}
		return list.ToArray();
	}
}
