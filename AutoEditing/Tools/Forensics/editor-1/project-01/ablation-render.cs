using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    static Vegas VegasRef;
    static Effect Shake1, Shake2, Flicker, DistortRGB;
    static OFXBooleanParameter Mb1, Mb2;
    static bool OrigShake1Bypass, OrigShake2Bypass, OrigFlickerBypass, OrigDistortBypass, OrigMb1, OrigMb2;
    static Timecode SnapTime;
    static string OutDir = @"C:\VEGAS\ablation-frames";
    static List<string> Log = new List<string>();

    public void FromVegas(Vegas vegas)
    {
        Directory.CreateDirectory(OutDir);
        VegasRef = vegas;

        try
        {
            // Target: t1_e16 (track index 1, event index 16) -- a confirmed "impact" event.
            // Chain order (confirmed from inspection): 0=S_Shake(inst1) 1=S_Shake(inst2) 2=S_Flicker 3=S_DistortRGB
            Track track = vegas.Project.Tracks[1];
            VideoEvent ev = (VideoEvent)track.Events[16];
            SnapTime = Timecode.FromSeconds(43.28); // the "at" moment, ~53ms after cut, previously showed heavy blur
            Effects fx = ev.Effects;

            if (fx.Count != 4)
            {
                File.WriteAllText(Path.Combine(OutDir, "ablation-log.txt"), "ABORT: expected 4 effects on t1_e16, found " + fx.Count);
                return;
            }

            Shake1 = fx[0];
            Shake2 = fx[1];
            Flicker = fx[2];
            DistortRGB = fx[3];

            Mb1 = (OFXBooleanParameter)Shake1.OFXEffect.FindParameterByName("Motion Blur");
            Mb2 = (OFXBooleanParameter)Shake2.OFXEffect.FindParameterByName("Motion Blur");

            OrigShake1Bypass = Shake1.Bypass; OrigShake2Bypass = Shake2.Bypass;
            OrigFlickerBypass = Flicker.Bypass; OrigDistortBypass = DistortRGB.Bypass;
            OrigMb1 = Mb1.Value; OrigMb2 = Mb2.Value;

            Reset();
            Shot("01_full_original_chain");

            Reset(); Shake1.Bypass = true;
            Shot("02_shake1_disabled");

            Reset(); Shake2.Bypass = true;
            Shot("03_shake2_disabled");

            Reset(); Mb1.Value = false;
            Shot("04_shake1_motionblur_off");

            Reset(); Mb2.Value = false;
            Shot("05_shake2_motionblur_off");

            Reset(); Mb1.Value = false; Mb2.Value = false;
            Shot("06_both_motionblur_off");

            Reset(); Flicker.Bypass = true;
            Shot("07_flicker_disabled");

            Reset(); DistortRGB.Bypass = true;
            Shot("08_distortRGB_disabled");

            Reset(); Shake1.Bypass = true; Shake2.Bypass = true;
            Shot("09_both_shakes_disabled");

            Reset(); Shake2.Bypass = true; Flicker.Bypass = true; DistortRGB.Bypass = true;
            Shot("10_shake1_isolated");

            Reset(); Shake1.Bypass = true; Flicker.Bypass = true; DistortRGB.Bypass = true;
            Shot("11_shake2_isolated");

            Reset(); Shake1.Bypass = true; Shake2.Bypass = true; Flicker.Bypass = true;
            Shot("12_distortRGB_isolated");

            Reset(); Shake1.Bypass = true; Shake2.Bypass = true; Flicker.Bypass = true; DistortRGB.Bypass = true;
            Shot("13_no_event_effects_baseline");

            Reset();
            RenderStatus s2 = vegas.SaveSnapshot(Path.Combine(OutDir, "14_ordinary_chain_t1_e3_reference.png"), ImageFileFormat.PNG, Timecode.FromSeconds(36.05));
            Log.Add("14_ordinary_chain_t1_e3_reference -> status=" + s2);

            Reset();
            Log.Add("Restored original state on t1_e16. Verify: shake1.Bypass=" + Shake1.Bypass + " shake2.Bypass=" + Shake2.Bypass +
                " flicker.Bypass=" + Flicker.Bypass + " distortRGB.Bypass=" + DistortRGB.Bypass +
                " mb1=" + Mb1.Value + " mb2=" + Mb2.Value);
            Log.Add("Project.IsModified after ablation (should be true in-memory, but NEVER saved): " + vegas.Project.IsModified);
        }
        catch (Exception ex)
        {
            Log.Add("EXCEPTION: " + ex);
        }

        File.WriteAllLines(Path.Combine(OutDir, "ablation-log.txt"), Log.ToArray());
    }

    static void Reset()
    {
        Shake1.Bypass = OrigShake1Bypass;
        Shake2.Bypass = OrigShake2Bypass;
        Flicker.Bypass = OrigFlickerBypass;
        DistortRGB.Bypass = OrigDistortBypass;
        Mb1.Value = OrigMb1;
        Mb2.Value = OrigMb2;
    }

    static void Shot(string label)
    {
        string file = Path.Combine(OutDir, label + ".png");
        RenderStatus status = VegasRef.SaveSnapshot(file, ImageFileFormat.PNG, SnapTime);
        bool exists = File.Exists(file);
        Log.Add(label + " -> status=" + status + " fileExists=" + exists);
    }
}
