using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    static List<string> Diag = new List<string>();

    public void FromVegas(Vegas vegas)
    {
        string logPath = @"C:\VEGAS\editor-2-project-01-analysis\diagnostics\remove-big-avi-result.txt";
        try
        {
            Project project = vegas.Project;
            Tracks tracks = project.Tracks;
            int removed = 0;

            for (int t = 0; t < tracks.Count; t++)
            {
                Track track = tracks[t];
                TrackEvents events = track.Events;
                List<TrackEvent> toRemove = new List<TrackEvent>();
                for (int e = 0; e < events.Count; e++)
                {
                    TrackEvent ev = events[e];
                    bool usesBigAvi = false;
                    try
                    {
                        Takes takes = ev.Takes;
                        for (int k = 0; k < takes.Count; k++)
                        {
                            string mp = takes[k].MediaPath ?? "";
                            if (mp.EndsWith("1.avi", StringComparison.OrdinalIgnoreCase))
                            {
                                usesBigAvi = true;
                                Diag.Add("Found event using 1.avi: track=" + t + " eventIndex=" + e + " start=" + ev.Start.ToString() + " path=" + mp);
                            }
                        }
                    }
                    catch (Exception ex) { Diag.Add("Take scan failed track=" + t + " event=" + e + ": " + ex.Message); }
                    if (usesBigAvi) toRemove.Add(ev);
                }
                foreach (TrackEvent ev in toRemove)
                {
                    try
                    {
                        track.Events.Remove(ev);
                        removed++;
                    }
                    catch (Exception ex) { Diag.Add("Remove failed: " + ex.Message); }
                }
            }

            Diag.Add("Total events removed: " + removed);

            string newPath = @"C:\VEGAS\Glovali Montage 5\Glovali Montage 5\Untitled.relinked2.noavi.veg";
            bool saved = vegas.Project.SaveProject(newPath);
            Diag.Add("saved=" + saved + " path=" + newPath);

            File.WriteAllLines(logPath, Diag.ToArray());
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(logPath, "FATAL: " + ex); } catch { }
        }
    }
}
