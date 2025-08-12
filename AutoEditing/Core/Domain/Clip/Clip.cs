using System.Collections.Generic;
using ScriptPortal.Vegas;

namespace Core.Domain.Clip
{
    public class Clip
    {
        public string FilePath { get; set; }
        public bool IsOpener { get; set; }
        public bool IsCloser { get; set; }
        public string PlayerName { get; set; }
        public string Game { get; set; }
        public string Map { get; set; }
        public string Gun { get; set; }
        public string ClipType { get; set; }
        public int SequenceNumber { get; set; }
        public VideoEvent VideoEvent { get; set; }  // Assigned after placement
        public List<Timecode> Kills { get; set; } = new List<Timecode>(); // Store detected kills
    }
}
