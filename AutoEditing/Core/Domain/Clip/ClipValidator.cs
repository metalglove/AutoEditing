using System;
using System.Linq;
using ScriptPortal.Vegas;

namespace Core.Domain.Clip
{
    public class ClipValidator
    {
        public bool Validate(Clip clip, Vegas vegas)
        {
            try
            {
                var media = vegas.Project.MediaPool.AddMedia(clip.FilePath);
                if (media == null) return false;

                var videoStream = media.Streams.OfType<VideoStream>().FirstOrDefault();
                if (videoStream == null || videoStream.FrameRate < 60) return false;

                // Bitrate check (approximate; VEGAS API doesn't expose bitrate directly, so placeholder)
                // For real impl, use FFProbe or external tool via Process.Start
                // Assume true for MVP
                bool bitrateValid = true;  // Replace with actual check

                // Kill detection validation (optional, call KillDetector)
                return bitrateValid;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string[] GetValidationErrors(Clip clip)
        {
            // Implement error collection
            var errors = new System.Collections.Generic.List<string>();
            
            if (string.IsNullOrEmpty(clip.FilePath))
                errors.Add("File path is empty");
            
            if (!System.IO.File.Exists(clip.FilePath))
                errors.Add("File does not exist");
            
            // Add more validation logic as needed
            return errors.ToArray();
        }
    }
}
