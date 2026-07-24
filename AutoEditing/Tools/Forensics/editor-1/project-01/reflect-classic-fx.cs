using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string outDir = @"C:\Users\mario\AppData\Local\Temp\claude\C--Users-mario-sources-AutoEditing\cfd2a529-0192-4e0c-bcae-f69fd4b57d68\scratchpad";
        Assembly asm = typeof(Vegas).Assembly;

        string[] names = new string[] { "Keyframe", "Keyframes", "AudioBusTrack", "VideoBusTrack", "BusTrack" };
        StringBuilder sb = new StringBuilder();
        foreach (string n in names)
        {
            Type t = asm.GetType("ScriptPortal.Vegas." + n);
            if (t == null) { sb.AppendLine(n + ": NOT FOUND"); continue; }
            sb.AppendLine("=== " + t.FullName + " ===");
            if (t.BaseType != null) sb.AppendLine("  base: " + t.BaseType.FullName);
            PropertyInfo[] props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Array.Sort(props, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            foreach (PropertyInfo p in props)
            {
                sb.AppendLine("  prop " + p.PropertyType.Name + " " + p.Name + " {" + (p.CanRead ? "get" : "") + (p.CanWrite ? "/set" : "") + "}");
            }
            MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (MethodInfo m in methods)
            {
                if (m.IsSpecialName) continue;
                List<string> ps = new List<string>();
                foreach (ParameterInfo pi in m.GetParameters()) ps.Add(pi.ParameterType.Name + " " + pi.Name);
                sb.AppendLine("  method " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", ps.ToArray()) + ")");
            }
            sb.AppendLine();
        }

        // Also: does Project have MasterBus / VideoBus and what's their exact declared type
        Type projType = typeof(Project);
        PropertyInfo mb = projType.GetProperty("MasterBus");
        PropertyInfo vb = projType.GetProperty("VideoBus");
        sb.AppendLine("Project.MasterBus declared type: " + (mb != null ? mb.PropertyType.FullName : "NOT FOUND"));
        sb.AppendLine("Project.VideoBus declared type: " + (vb != null ? vb.PropertyType.FullName : "NOT FOUND"));

        File.WriteAllText(Path.Combine(outDir, "classic-fx-members.txt"), sb.ToString());
    }
}
