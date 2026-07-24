using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    static List<string> Diag = new List<string>();

    // Filenames (matched by EndsWith, case-insensitive) that are either too large to safely
    // process automatically (1.avi, ~3.9GB, previously triggered a VEGAS crash on load once
    // resolvable) or have no available local copy at all (the dpita Videos-folder DVR capture).
    static string[] ProblemFilenames = new string[]
    {
        "1.avi",
        "Call of Duty  Modern Warfare 2 (2022) 2023.03.31 - 19.45.39.08.DVR.mp4"
    };

    public void FromVegas(Vegas vegas)
    {
        string logPath = @"C:\VEGAS\editor-2-project-01-analysis\diagnostics\remove-problem-media-result.txt";
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
                    bool isProblem = false;
                    string matchedPath = null;
                    try
                    {
                        Takes takes = ev.Takes;
                        for (int k = 0; k < takes.Count; k++)
                        {
                            string mp = takes[k].MediaPath ?? "";
                            foreach (string problem in ProblemFilenames)
                            {
                                if (mp.EndsWith(problem, StringComparison.OrdinalIgnoreCase))
                                {
                                    isProblem = true;
                                    matchedPath = mp;
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Diag.Add("Take scan failed track=" + t + " event=" + e + ": " + ex.Message); }
                    if (isProblem)
                    {
                        Diag.Add("Found problem event: track=" + t + " eventIndex=" + e + " start=" + ev.Start.ToString() + " path=" + matchedPath);
                        toRemove.Add(ev);
                    }
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

            // The dpita-Videos DVR file with no local copy has no match here as a track
            // event -- it is very likely present in the Media Pool but never placed on the
            // timeline. RemoveUnusedMedia() clears any such orphaned pool entries (including
            // 1.avi's now-unused entry after its one event was just removed above), which
            // should stop the missing-media dialog from recurring on future opens.
            try
            {
                project.MediaPool.RemoveUnusedMedia();
                Diag.Add("RemoveUnusedMedia() called successfully.");
            }
            catch (Exception ex) { Diag.Add("RemoveUnusedMedia() failed: " + ex.Message); }

            string newPath = @"C:\VEGAS\Glovali Montage 5\Glovali Montage 5\Untitled.clean.veg";
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
