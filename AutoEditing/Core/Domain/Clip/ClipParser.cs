using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Core.Domain.Clip
{
    public class ClipParser
    {
        public List<Clip> ParseAllClips(string folderPath)
        {
            List<Clip> clips = new List<Clip>();
            foreach (string file in Directory.GetFiles(folderPath, "*.mp4"))
            {
                clips.Add(ParseClip(file));
            }
            return clips;
        }

        public Clip ParseClip(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string[] parts = fileName.Split('-').Select(p => p.Trim()).ToArray();

            Clip clip = new Clip { FilePath = filePath };

            // Handle prefix
            if (parts[0].StartsWith("[OPENER]"))
            {
                clip.IsOpener = true;
                parts[0] = parts[0].Replace("[OPENER]", "");
            }
            else if (parts[0].StartsWith("[CLOSER]"))
            {
                clip.IsCloser = true;
                parts[0] = parts[0].Replace("[CLOSER]", "");
            }

            // Assign metadata (assuming 6 parts after prefix handling)
            if (parts.Length >= 5)
            {
                clip.PlayerName = parts[0];
                clip.Game = parts[1];
                clip.Map = parts[2];
                clip.Gun = parts[3];
                clip.ClipType = parts[4];
                clip.SequenceNumber = 1;

                if (parts.Length > 5)
                {
                    if (int.TryParse(parts[5], out int sequenceNumber))
                    {
                        clip.SequenceNumber = 1;
                        clip.SequenceNumber = sequenceNumber;
                    }
                }
            }

            return clip;
        }
    }
}
