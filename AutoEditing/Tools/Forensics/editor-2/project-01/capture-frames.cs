using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string outDir = @"C:\VEGAS\editor-2-project-01-analysis\frames\representative";
        Directory.CreateDirectory(outDir);
        List<string> log = new List<string>();
        try
        {
            string[] labels = new string[] {
                "01_intro_title_editor-2_presents", "02_intro_title_glovaliin", "03_gap_blank_at_9",
                "04_opener_shake_hit", "05_strobe_flash_burst", "06_multihit_burst_start",
                "07_multihit_burst_mid", "08_filmdamage_texture", "09_outro_lighten_composite",
                "10_final_frame"
            };
            double[] times = new double[] { 2.0, 6.5, 9.0, 10.8, 70.7, 125.3, 129.0, 178.0, 180.0, 184.0 };
            for (int i = 0; i < labels.Length; i++)
            {
                string path = Path.Combine(outDir, labels[i] + ".png");
                RenderStatus status = vegas.SaveSnapshot(path, ImageFileFormat.PNG, Timecode.FromSeconds(times[i]));
                log.Add(labels[i] + " @ " + times[i] + "s -> status=" + status + " fileExists=" + File.Exists(path));
            }
        }
        catch (Exception ex)
        {
            log.Add("EXCEPTION: " + ex);
        }
        File.WriteAllLines(@"C:\VEGAS\editor-2-project-01-analysis\diagnostics\capture-frames-log.txt", log.ToArray());
    }
}
