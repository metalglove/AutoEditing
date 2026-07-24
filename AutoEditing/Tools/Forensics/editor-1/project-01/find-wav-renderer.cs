using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        List<string> log = new List<string>();
        try
        {
            Renderers renderers = vegas.Renderers;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer r = renderers[i];
                log.Add("Renderer[" + i + "]: " + r.Name);
                RenderTemplates templates = r.Templates;
                for (int j = 0; j < templates.Count && j < 8; j++)
                {
                    RenderTemplate t = templates[j];
                    log.Add("  Template: " + t.Name + " | ext=" + string.Join(",", t.FileExtensions) +
                        " | audioOnly(videoStreamCount=0)=" + (t.VideoStreamCount == 0) +
                        " | sampleRate=" + t.AudioSampleRate + " bits=" + t.AudioBitsPerSample + " ch=" + t.AudioChannelCount);
                }
            }
        }
        catch (Exception ex)
        {
            log.Add("EXCEPTION: " + ex);
        }
        File.WriteAllLines(@"C:\VEGAS\renderer-inventory.txt", log.ToArray());
    }
}
