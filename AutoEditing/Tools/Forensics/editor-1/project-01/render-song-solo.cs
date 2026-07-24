using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        List<string> log = new List<string>();
        string outFile = @"C:\VEGAS\song-solo.wav";
        bool[] origMute = null;
        Tracks tracks = null;
        try
        {
            Project project = vegas.Project;
            tracks = project.Tracks;
            origMute = new bool[tracks.Count];
            for (int i = 0; i < tracks.Count; i++)
            {
                origMute[i] = tracks[i].Mute;
                // Mute everything except track index 3 (the song track).
                tracks[i].Mute = (i != 3);
            }

            Renderer renderer = vegas.Renderers.FindByName("Wave (Microsoft)");
            RenderTemplate template = renderer.Templates.FindByName("48.000 Hz, 16 Bit, Stereo, PCM");
            log.Add("Using renderer=" + renderer.Name + " template=" + template.Name + " sr=" + template.AudioSampleRate + " bits=" + template.AudioBitsPerSample);

            RenderStatus status = project.Render(outFile, template);
            log.Add("Render status: " + status + " fileExists=" + File.Exists(outFile) +
                (File.Exists(outFile) ? (" size=" + new FileInfo(outFile).Length) : ""));
        }
        catch (Exception ex)
        {
            log.Add("EXCEPTION: " + ex);
        }
        finally
        {
            if (tracks != null && origMute != null)
            {
                for (int i = 0; i < tracks.Count; i++) tracks[i].Mute = origMute[i];
                log.Add("Restored original mute state on all tracks.");
            }
        }
        File.WriteAllLines(@"C:\VEGAS\render-song-solo-log.txt", log.ToArray());
    }
}
