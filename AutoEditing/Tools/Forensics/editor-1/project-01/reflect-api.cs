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
        try
        {
            allTypes = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException rtle)
        {
            List<Type> ok = new List<Type>();
            foreach (Type t in rtle.Types)
            {
                if (t != null) ok.Add(t);
            }
            allTypes = ok.ToArray();
        }

        List<Type> vegasNsTypesList = new List<Type>();
        foreach (Type t in allTypes)
        {
            if (t.IsPublic && t.Namespace == "ScriptPortal.Vegas")
                vegasNsTypesList.Add(t);
        }
        vegasNsTypesList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        Type[] vegasNsTypes = vegasNsTypesList.ToArray();

        StringBuilder sbList = new StringBuilder();
        foreach (Type t in vegasNsTypes)
        {
            string kind = t.IsEnum ? "enum" : (t.IsInterface ? "interface" : (t.IsValueType ? "struct" : "class"));
            sbList.AppendLine(kind + " " + t.Name + (t.BaseType != null ? " : " + t.BaseType.Name : ""));
        }
        File.WriteAllText(Path.Combine(outDir, "api-types.txt"), sbList.ToString());

        // Curated set of types we care about for the inspector, plus anything containing key keywords
        string[] wantedNames = new string[]
        {
            "Vegas", "Project", "ProjectVideo", "ProjectAudio", "ProjectRuler", "VideoRenderer",
            "Track", "VideoTrack", "AudioTrack", "TrackBusMode", "TrackMotion", "TrackMotionKeyframe",
            "TrackEvent", "VideoEvent", "AudioEvent", "TrackEventGroup",
            "Take", "Media", "MediaStream", "VideoStream", "AudioStream", "MediaMarker", "MediaRegion",
            "Effect", "EffectChain", "PlugInNode", "Preset", "OFXEffect",
            "Envelope", "EnvelopePoint", "EnvelopeType", "CurveType",
            "VideoMotion", "VideoMotionKeyframe", "VideoMotionBounds", "VideoMotionVertex", "VideoMotionType",
            "Marker", "Region",
            "Timecode", "RulerFormat",
            "Bus", "AudioBus", "VideoBus", "Mixer",
            "RenderTemplate", "RenderArgs",
            "Fade", "FadeType", "Resample", "ResampleMode",
            "VideoStreamProperties", "AudioStreamProperties",
            "Loop", "PlaybackRate", "Velocity", "VelocityEnvelope",
            "CompositeMode", "MediaType", "InterpolationType", "KeyframeType"
        };

        List<Type> typesToDumpList = new List<Type>();
        foreach (Type t in vegasNsTypes)
        {
            bool matched = false;
            foreach (string w in wantedNames)
            {
                if (t.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched = true;
                    break;
                }
            }
            if (matched) typesToDumpList.Add(t);
        }
        typesToDumpList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        Type[] typesToDump = typesToDumpList.ToArray();

        StringBuilder sbMembers = new StringBuilder();
        foreach (Type t in typesToDump)
        {
            sbMembers.AppendLine("=== " + t.FullName + " ===");
            if (t.IsEnum)
            {
                sbMembers.AppendLine("  enum values: " + string.Join(", ", Enum.GetNames(t)));
            }
            else
            {
                var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                             ;
                Array.Sort(props, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                foreach (PropertyInfo p in props)
                {
                    ParameterInfo[] ips = p.GetIndexParameters();
                    string idx = "";
                    if (ips.Length > 0)
                    {
                        StringBuilder idxSb = new StringBuilder("[");
                        for (int i = 0; i < ips.Length; i++)
                        {
                            if (i > 0) idxSb.Append(",");
                            idxSb.Append(ips[i].ParameterType.Name);
                        }
                        idxSb.Append("]");
                        idx = idxSb.ToString();
                    }
                    string acc = (p.CanRead ? "get" : "") + (p.CanWrite ? "/set" : "");
                    sbMembers.AppendLine("  prop  " + p.PropertyType.Name + " " + p.Name + idx + " {" + acc + "}");
                }
                MethodInfo[] allMethods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                List<MethodInfo> methodsList = new List<MethodInfo>();
                foreach (MethodInfo m in allMethods)
                {
                    if (!m.IsSpecialName) methodsList.Add(m);
                }
                methodsList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                foreach (MethodInfo m in methodsList)
                {
                    ParameterInfo[] mps = m.GetParameters();
                    StringBuilder psSb = new StringBuilder();
                    for (int i = 0; i < mps.Length; i++)
                    {
                        if (i > 0) psSb.Append(", ");
                        psSb.Append(mps[i].ParameterType.Name + " " + mps[i].Name);
                    }
                    sbMembers.AppendLine("  method " + m.ReturnType.Name + " " + m.Name + "(" + psSb.ToString() + ")");
                }
                FieldInfo[] fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                Array.Sort(fields, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                foreach (FieldInfo f in fields)
                {
                    sbMembers.AppendLine("  field " + f.FieldType.Name + " " + f.Name);
                }
                if (t.BaseType != null && t.BaseType.Namespace == "ScriptPortal.Vegas")
                {
                    sbMembers.AppendLine("  (inherits: " + t.BaseType.Name + ")");
                }
                Type[] interfaces = t.GetInterfaces();
                if (interfaces.Length > 0)
                {
                    StringBuilder ifaceSb = new StringBuilder();
                    for (int i = 0; i < interfaces.Length; i++)
                    {
                        if (i > 0) ifaceSb.Append(", ");
                        ifaceSb.Append(interfaces[i].Name);
                    }
                    sbMembers.AppendLine("  (implements: " + ifaceSb.ToString() + ")");
                }
            }
            sbMembers.AppendLine();
        }
        File.WriteAllText(Path.Combine(outDir, "api-members.txt"), sbMembers.ToString());

        File.WriteAllText(Path.Combine(outDir, "reflect-done.txt"),
            "types=" + vegasNsTypes.Length + " dumped=" + typesToDump.Length);
    }
}
