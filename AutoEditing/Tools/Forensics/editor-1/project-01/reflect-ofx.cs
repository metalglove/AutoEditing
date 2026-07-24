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
        Type[] allTypes;
        try { allTypes = asm.GetTypes(); }
        catch (ReflectionTypeLoadException rtle)
        {
            List<Type> ok = new List<Type>();
            foreach (Type t in rtle.Types) if (t != null) ok.Add(t);
            allTypes = ok.ToArray();
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < allTypes.Length; i++)
        {
            Type t = allTypes[i];
            if (t.Namespace != "ScriptPortal.Vegas") continue;
            if (t.Name.IndexOf("OFX", StringComparison.OrdinalIgnoreCase) < 0) continue;

            sb.AppendLine("=== " + t.FullName + " ===");
            if (t.IsEnum)
            {
                sb.AppendLine("  enum values: " + string.Join(", ", Enum.GetNames(t)));
                sb.AppendLine();
                continue;
            }
            if (t.BaseType != null) sb.AppendLine("  base: " + t.BaseType.FullName);

            PropertyInfo[] props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            Array.Sort(props, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            foreach (PropertyInfo p in props)
            {
                if (p.GetIndexParameters().Length > 0)
                {
                    sb.AppendLine("  indexer[" + string.Join(",", Array.ConvertAll(p.GetIndexParameters(), ip => ip.ParameterType.Name)) + "] -> " + p.PropertyType.Name);
                    continue;
                }
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

        File.WriteAllText(Path.Combine(outDir, "ofx-members.txt"), sb.ToString());
    }
}
