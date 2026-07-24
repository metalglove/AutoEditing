using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string outDir = @"C:\VEGAS\representative-frames";
        Directory.CreateDirectory(outDir);
        string logPath = Path.Combine(outDir, "capture-log.txt");
        List<string> log = new List<string>();

        // (label, seconds) -- picked from project-inspection-v3.json analysis.
        var shots = new List<Tuple<string, double>>
        {
            Tuple.Create("00_pre-song-blackfield", 10.0),
            Tuple.Create("01_cinematic-bridge_t1_e0", 26.8),
            Tuple.Create("02_no-effects-counterexample_t1_e2", 35.4),
            Tuple.Create("03_opener-dip_t1_e3_pre", 35.90),
            Tuple.Create("03_opener-dip_t1_e3_at", 36.05),
            Tuple.Create("03_opener-dip_t1_e3_post", 36.35),
            Tuple.Create("04_impact-distortRGB_t1_e16_pre", 43.10),
            Tuple.Create("04_impact-distortRGB_t1_e16_at", 43.28),
            Tuple.Create("04_impact-distortRGB_t1_e16_post", 43.55),
            Tuple.Create("04_impact-distortRGB_t1_e16_settled", 43.90),
            Tuple.Create("05_black-and-white_t1_e113", 116.9),
            Tuple.Create("06_closer-triple_e272_hit1", 234.6),
            Tuple.Create("06_closer-triple_e273_beat", 235.6),
            Tuple.Create("06_closer-triple_e274_hit2_at", 236.35),
            Tuple.Create("06_closer-triple_e274_hit2_post", 236.6),
            Tuple.Create("07_final-event_t1_e275", 237.25),
        };

        int ok = 0, fail = 0;
        foreach (var shot in shots)
        {
            string label = shot.Item1;
            double seconds = shot.Item2;
            string file = Path.Combine(outDir, label + ".png");
            try
            {
                Timecode tc = Timecode.FromSeconds(seconds);
                RenderStatus status = vegas.SaveSnapshot(file, ImageFileFormat.PNG, tc);
                bool exists = File.Exists(file);
                log.Add(label + " @ " + seconds.ToString("0.000") + "s -> status=" + status + " fileExists=" + exists);
                if (exists) ok++; else fail++;
            }
            catch (Exception ex)
            {
                log.Add(label + " @ " + seconds.ToString("0.000") + "s -> EXCEPTION: " + ex.Message);
                fail++;
            }
        }

        log.Add("TOTAL: ok=" + ok + " fail=" + fail);
        File.WriteAllLines(logPath, log.ToArray());
    }
}
