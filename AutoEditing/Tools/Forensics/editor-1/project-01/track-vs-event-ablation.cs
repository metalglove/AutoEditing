using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    static Vegas VegasRef;
    static Effect TrackShake, TrackFlicker;
    static Effects EventFx; // the 4 (or however many) effects on the target event
    static bool[] OrigEventFxBypass;
    static bool OrigTrackShakeBypass, OrigTrackFlickerBypass;
    static string OutDir = @"C:\VEGAS\track-vs-event-frames";
    static List<string> Log = new List<string>();

    public void FromVegas(Vegas vegas)
    {
        Directory.CreateDirectory(OutDir);
        VegasRef = vegas;
        try
        {
            Track track = vegas.Project.Tracks[1];
            TrackShake = track.Effects[0];   // confirmed order: S_Shake, S_Flicker
            TrackFlicker = track.Effects[1];
            OrigTrackShakeBypass = TrackShake.Bypass;
            OrigTrackFlickerBypass = TrackFlicker.Bypass;

            RunForEvent((VideoEvent)track.Events[16], Timecode.FromSeconds(43.28), "impact_t1_e16");
            RunForEvent((VideoEvent)track.Events[3], Timecode.FromSeconds(36.05), "ordinary_t1_e3");

            TrackShake.Bypass = OrigTrackShakeBypass;
            TrackFlicker.Bypass = OrigTrackFlickerBypass;
            Log.Add("Restored track-level bypass. shake=" + TrackShake.Bypass + " flicker=" + TrackFlicker.Bypass);
        }
        catch (Exception ex)
        {
            Log.Add("EXCEPTION: " + ex);
        }
        File.WriteAllLines(Path.Combine(OutDir, "track-vs-event-log.txt"), Log.ToArray());
    }

    static void RunForEvent(VideoEvent ev, Timecode snapTime, string label)
    {
        EventFx = ev.Effects;
        OrigEventFxBypass = new bool[EventFx.Count];
        for (int i = 0; i < EventFx.Count; i++) OrigEventFxBypass[i] = EventFx[i].Bypass;

        // A: track ON, event ON (natural state)
        ResetAll();
        Shot(label + "_A_trackON_eventON", snapTime);

        // B: track OFF, event ON
        ResetAll(); TrackShake.Bypass = true; TrackFlicker.Bypass = true;
        Shot(label + "_B_trackOFF_eventON", snapTime);

        // C: track ON, event OFF
        ResetAll(); SetAllEventFx(true);
        Shot(label + "_C_trackON_eventOFF", snapTime);

        // D: track OFF, event OFF
        ResetAll(); TrackShake.Bypass = true; TrackFlicker.Bypass = true; SetAllEventFx(true);
        Shot(label + "_D_trackOFF_eventOFF", snapTime);

        ResetAll();
    }

    static void SetAllEventFx(bool bypass)
    {
        for (int i = 0; i < EventFx.Count; i++) EventFx[i].Bypass = bypass;
    }

    static void ResetAll()
    {
        TrackShake.Bypass = OrigTrackShakeBypass;
        TrackFlicker.Bypass = OrigTrackFlickerBypass;
        for (int i = 0; i < EventFx.Count; i++) EventFx[i].Bypass = OrigEventFxBypass[i];
    }

    static void Shot(string label, Timecode t)
    {
        string file = Path.Combine(OutDir, label + ".png");
        RenderStatus status = VegasRef.SaveSnapshot(file, ImageFileFormat.PNG, t);
        Log.Add(label + " -> status=" + status + " fileExists=" + File.Exists(file));
    }
}
