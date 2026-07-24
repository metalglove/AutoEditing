using System;
using System.IO;
using System.Reflection;
using System.Text;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        StringBuilder sb = new StringBuilder();
        Type[] types = new Type[] { typeof(MediaPool), typeof(Media) };
        foreach (Type t in types)
        {
            sb.AppendLine("=== " + t.FullName + " ===");
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                sb.AppendLine("  method " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(",", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name)) + ")");
            }
        }
        File.WriteAllText(@"C:\VEGAS\editor-2-project-01-analysis\diagnostics\mediapool-reflect.txt", sb.ToString());
    }
}
