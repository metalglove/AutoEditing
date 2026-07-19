using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Domain.Clip
{
    /// <summary>
    /// Parses clip filenames into <see cref="Clip"/> metadata.
    ///
    /// Supported convention (dash-separated):
    ///   [PREFIX]Player - Game - Map - Details.mp4
    ///
    /// The Details section is space-separated and packs the remaining metadata:
    ///   GUN [TYPE...] [SEQUENCE] [(notes)]
    ///
    /// Examples:
    ///   Glovali - MWIII - Dome - MORS 6ON 001.mp4
    ///   Glovali - MWIII - Greece - XRK QUAD.mp4
    ///   Glovali - MWIII - AFGHAN - MORS 5ON Triple Ender (7mult).mp4
    ///   Glovali - MWIII - Rio - KATT 5ON X2 001 (Triple).mp4
    ///
    /// The legacy [OPENER] and [CLOSER] filename prefixes are the only way to mark
    /// a clip for the start or end of the montage. Words inside the details section
    /// (e.g. "Ender" describing a game-ending kill) are just part of the clip type
    /// and do not affect placement.
    ///
    /// The legacy convention with gun and type as their own dash-separated parts
    /// (Player - Game - Map - Gun - Type - Sequence.mp4) is still supported.
    /// </summary>
    public class ClipParser
    {
        private static readonly Regex NotesRegex = new Regex(@"\(([^)]*)\)", RegexOptions.Compiled);

        public List<Clip> ParseAllClips(string folderPath)
        {
            List<Clip> clips = new List<Clip>();
            foreach (string file in Directory.GetFiles(folderPath, "*.mp4"))
            {
                Clip clip = ParseClip(file);
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }
            return clips;
        }

        /// <summary>
        /// Parses a single clip file. Returns null when the filename does not
        /// match any supported convention.
        /// </summary>
        public Clip ParseClip(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            Clip clip = new Clip { FilePath = filePath };

            // Legacy [OPENER]/[CLOSER] prefixes.
            if (fileName.StartsWith("[OPENER]", StringComparison.OrdinalIgnoreCase))
            {
                clip.IsOpener = true;
                fileName = fileName.Substring("[OPENER]".Length);
            }
            else if (fileName.StartsWith("[CLOSER]", StringComparison.OrdinalIgnoreCase))
            {
                clip.IsCloser = true;
                fileName = fileName.Substring("[CLOSER]".Length);
            }

            string[] parts = fileName.Split('-').Select(p => p.Trim()).ToArray();

            // Need at least Player - Game - Map - Details.
            if (parts.Length < 4 || parts.Take(4).Any(string.IsNullOrWhiteSpace))
            {
                return null;
            }

            clip.PlayerName = parts[0];
            clip.Game = parts[1];
            clip.Map = parts[2];
            clip.SequenceNumber = 1;

            if (parts.Length >= 5)
            {
                ParseLegacyParts(clip, parts);
            }
            else
            {
                ParseDetails(clip, parts[3]);
            }

            return string.IsNullOrWhiteSpace(clip.Gun) ? null : clip;
        }

        /// <summary>
        /// Legacy convention: Gun and ClipType are their own dash-separated parts,
        /// optionally followed by a sequence number part.
        /// </summary>
        private static void ParseLegacyParts(Clip clip, string[] parts)
        {
            clip.Gun = parts[3];
            clip.ClipType = parts[4];

            if (parts.Length > 5 && int.TryParse(parts[5], out int sequenceNumber) && sequenceNumber >= 1)
            {
                clip.SequenceNumber = sequenceNumber;
            }
        }

        /// <summary>
        /// Details convention: "GUN [TYPE...] [SEQUENCE] [(notes)]" packed into the
        /// final dash-separated part.
        /// </summary>
        private static void ParseDetails(Clip clip, string details)
        {
            // Pull out "(notes)" first so note text never pollutes the type tokens.
            Match notesMatch = NotesRegex.Match(details);
            if (notesMatch.Success)
            {
                clip.Notes = notesMatch.Groups[1].Value.Trim();
                details = NotesRegex.Replace(details, " ");
            }

            List<string> tokens = details
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (tokens.Count == 0)
            {
                return;
            }

            clip.Gun = tokens[0];
            tokens.RemoveAt(0);

            List<string> typeTokens = new List<string>();
            foreach (string token in tokens)
            {
                if (IsSequenceNumber(token, out int sequenceNumber))
                {
                    clip.SequenceNumber = sequenceNumber;
                }
                else
                {
                    typeTokens.Add(token);
                }
            }

            clip.ClipType = typeTokens.Count > 0 ? string.Join(" ", typeTokens) : "Clip";
        }

        /// <summary>
        /// Sequence numbers are zero-padded counters such as "001". A bare number
        /// without padding is only treated as a sequence when it is small; tokens
        /// like "X2" or "7ON" never reach this check because they are not numeric.
        /// </summary>
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
}
