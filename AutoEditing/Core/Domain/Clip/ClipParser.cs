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
                if (IsValidFileName(file))
                {
                    clips.Add(ParseClip(file));
                }
            }
            return clips;
        }

        private bool IsValidFileName(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string[] parts = fileName.Split('-').Select(p => p.Trim()).ToArray();

            // Remove prefix if present
            if (parts.Length > 0 && (parts[0].StartsWith("[OPENER]") || parts[0].StartsWith("[CLOSER]")))
            {
                parts[0] = parts[0].Replace("[OPENER]", "").Replace("[CLOSER]", "").Trim();
            }

            // Must have at least 5 parts: PlayerName, Game, Map, Gun, ClipType
            if (parts.Length < 5)
            {
                return false;
            }

            // Check that required parts are not empty
            for (int i = 0; i < 5; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                {
                    return false;
                }
            }

            // If there's a 6th part, it should be a valid sequence number
            if (parts.Length > 5)
            {
                if (!int.TryParse(parts[5], out int sequenceNumber) || sequenceNumber < 1)
                {
                    return false;
                }
            }

            return true;
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
