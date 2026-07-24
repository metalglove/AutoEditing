using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    static Effect BlurMo;
    static Effect Glow;
    static bool origBlurMoBypass;
    static bool origGlowBypass;
    static List<string> log = new List<string>();

    static void Reset()
    {
        BlurMo.Bypass = origBlurMoBypass;
        Glow.Bypass = origGlowBypass;
    }

    static void Shot(Vegas vegas, string label, Timecode tc)
    {
        string path = @"C:\VEGAS\ordinary-ablation-frames\" + label + ".png";
        vegas.SaveSnapshot(path, ImageFileFormat.PNG, tc);
        log.Add(label + " -> saved");
    }

    public void FromVegas(Vegas vegas)
    {
        Directory.CreateDirectory(@"C:\VEGAS\ordinary-ablation-frames");
        try
        {
            Project project = vegas.Project;
            Track track = project.Tracks[1];
            VideoEvent evt = (VideoEvent)track.Events[3];
            if (evt == null) { log.Add("EVENT NOT FOUND"); File.WriteAllLines(@"C:\VEGAS\ordinary-ablation-log.txt", log.ToArray()); return; }

            Effects effectsList = evt.Effects;
            foreach (Effect fx in effectsList)
            {
                if (fx.Description == "S_BlurMoCurves") BlurMo = fx;
                if (fx.Description == "S_Glow") Glow = fx;
            }
            if (BlurMo == null || Glow == null) { log.Add("EFFECTS NOT FOUND"); File.WriteAllLines(@"C:\VEGAS\ordinary-ablation-log.txt", log.ToArray()); return; }

            origBlurMoBypass = BlurMo.Bypass;
            origGlowBypass = Glow.Bypass;
            log.Add("orig BlurMo.Bypass=" + origBlurMoBypass + " Glow.Bypass=" + origGlowBypass);

            // Event start (from project-inspection-v3.json) = 35.98595s. Peak-effect timestamp: shortly after
            // cut, near Glow Brightness peak (2.0 at t=0, decaying to 0 by +0.767s) and BlurMoCurves Z-Dist
            // near its low point (0.96 at +0.167s). Use +0.08s as a compromise near-peak time.
            Timecode tcPeak = Timecode.FromSeconds(35.98595 + 0.08);

            // 1. Effects ON (original state, both active as authored)
            Shot(vegas, "01_ordinary_effects_on", tcPeak);

            // 2. Effects OFF (both bypassed)
            BlurMo.Bypass = true;
            Glow.Bypass = true;
            Shot(vegas, "02_ordinary_effects_off", tcPeak);

            // 3. Restore, shoot again to confirm restore worked
            Reset();
            Shot(vegas, "03_ordinary_effects_restored", tcPeak);

            log.Add("Final restore check: BlurMo.Bypass=" + BlurMo.Bypass + " Glow.Bypass=" + Glow.Bypass);
            log.Add("Project.IsModified=" + project.IsModified + " (expected true, in-memory only, never saved)");
        }
        catch (Exception ex)
        {
            log.Add("EXCEPTION: " + ex);
            try { Reset(); } catch { }
        }
        finally
        {
            File.WriteAllLines(@"C:\VEGAS\ordinary-ablation-log.txt", log.ToArray());
        }
    }
}
