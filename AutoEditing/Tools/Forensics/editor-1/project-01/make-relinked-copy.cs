using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

public class EntryPoint
{
    static List<string> Diag = new List<string>();
    static string[] MediaSearchRoots = new string[]
    {
        @"C:\VEGAS\montage 4 by editor-1",
        @"C:\VEGAS\montage 4 by editor-1\montage 4"
    };

    public void FromVegas(Vegas vegas)
    {
        string logPath = @"C:\Users\mario\AppData\Local\Temp\claude\C--Users-mario-sources-AutoEditing\cfd2a529-0192-4e0c-bcae-f69fd4b57d68\scratchpad\make-relinked-copy-result.txt";
        try
        {
            Project project = vegas.Project;
            List<string> allFiles = new List<string>();
            foreach (string root in MediaSearchRoots)
            {
                if (Directory.Exists(root))
                {
                    string[] found = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                    for (int i = 0; i < found.Length; i++) allFiles.Add(found[i]);
                }
            }

            MediaPool pool = project.MediaPool;
            List<Media> mediaList = new List<Media>();
            System.Collections.IDictionaryEnumerator en = pool.GetEnumerator();
            while (en.MoveNext())
            {
                Media m = en.Value as Media;
                if (m != null) mediaList.Add(m);
            }

            int relinked = 0, stillOffline = 0;
            for (int i = 0; i < mediaList.Count; i++)
            {
                Media media = mediaList[i];
                bool offline;
                try { offline = media.IsOffline(); } catch { continue; }
                if (!offline) continue;

                string originalPath = "";
                try { originalPath = media.FilePath; } catch { }
                string wantName = Path.GetFileName(originalPath);
                if (string.IsNullOrEmpty(wantName)) continue;

                string match = null;
                for (int f = 0; f < allFiles.Count; f++)
                {
                    if (string.Equals(Path.GetFileName(allFiles[f]), wantName, StringComparison.OrdinalIgnoreCase))
                    {
                        match = allFiles[f];
                        break;
                    }
                }
                if (match == null) { Diag.Add("NO MATCH: " + originalPath); continue; }

                try
                {
                    Media replacement = project.MediaPool.AddMedia(match);
                    if (replacement != null)
                    {
                        media.ReplaceWith(replacement);
                        relinked++;
                        Diag.Add("Relinked: " + originalPath + " -> " + match);
                    }
                }
                catch (Exception ex)
                {
                    Diag.Add("ReplaceWith threw for " + originalPath + ": " + ex.Message);
                    stillOffline++;
                }
            }

            string newPath = @"C:\VEGAS\montage 4 by editor-1\montage 4\Untitled.relinked.veg";
            bool saved = vegas.Project.SaveProject(newPath);

            using (StreamWriter w = new StreamWriter(logPath, false))
            {
                w.WriteLine("relinked=" + relinked + " stillOffline=" + stillOffline);
                w.WriteLine("saved=" + saved + " path=" + newPath);
                foreach (string d in Diag) w.WriteLine(d);
            }
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(logPath, "FATAL: " + ex); } catch { }
        }
    }
}
