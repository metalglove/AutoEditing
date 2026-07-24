using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;

public class EntryPoint
{
    static List<string> Diag = new List<string>();
    static string[] MediaSearchRoots = new string[]
    {
        @"C:\VEGAS\montage 4 by editor-1",
        @"C:\VEGAS\montage 4 by editor-1\montage 4"
    };

    // ---------- JSON primitives (hand-rolled: no Json.NET / LINQ available in script host) ----------

    static string JS(string s)
    {
        if (s == null) return "null";
        StringBuilder sb = new StringBuilder();
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
            else sb.Append(c);
        }
        sb.Append('"');
        return sb.ToString();
    }

    static string JN(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
        return d.ToString("0.######", CultureInfo.InvariantCulture);
    }

    static string JI(long l) { return l.ToString(CultureInfo.InvariantCulture); }
    static string JB(bool b) { return b ? "true" : "false"; }
    static string JNull() { return "null"; }

    static string Prop(string name, string jsonValue) { return JS(name) + ":" + (jsonValue ?? "null"); }

    static string Obj(List<string> props)
    {
        return "{" + String.Join(",", props.ToArray()) + "}";
    }

    static string Arr(List<string> items)
    {
        return "[" + String.Join(",", items.ToArray()) + "]";
    }

    static void LogDiag(string msg)
    {
        Diag.Add(msg);
    }

    static string TrySeconds(Timecode tc)
    {
        try { return JN(tc.ToMilliseconds() / 1000.0); }
        catch (Exception ex) { LogDiag("Timecode.ToMilliseconds failed: " + ex.Message); return JNull(); }
    }

    static double SecondsOf(Timecode tc)
    {
        try { return tc.ToMilliseconds() / 1000.0; }
        catch { return 0.0; }
    }

    // ---------- Entry point ----------

    public void FromVegas(Vegas vegas)
    {
        try
        {
            RelinkOfflineMedia(vegas);

            string projectJson = ExportProject(vegas);

            string outPath = @"C:\VEGAS\project-inspection.json";
            File.WriteAllText(outPath, projectJson, new UTF8Encoding(false));

            string diagPath = @"C:\VEGAS\project-inspection-diagnostics-raw.txt";
            File.WriteAllLines(diagPath, Diag.ToArray());
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    @"C:\VEGAS\project-inspection-FATAL.txt",
                    ex.ToString());
            }
            catch { }
        }
    }

    // ---------- Media relink ----------

    static void RelinkOfflineMedia(Vegas vegas)
    {
        Project project = vegas.Project;
        List<string> allFiles = new List<string>();
        foreach (string root in MediaSearchRoots)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    string[] found = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                    for (int i = 0; i < found.Length; i++) allFiles.Add(found[i]);
                }
                else
                {
                    LogDiag("Media search root does not exist: " + root);
                }
            }
            catch (Exception ex)
            {
                LogDiag("Error scanning media search root " + root + ": " + ex.Message);
            }
        }

        IDictionaryEnumeratorSafeIterate(project, allFiles);
    }

    static void IDictionaryEnumeratorSafeIterate(Project project, List<string> allFiles)
    {
        MediaPool pool = project.MediaPool;
        List<Media> mediaList = new List<Media>();
        System.Collections.IDictionaryEnumerator en = pool.GetEnumerator();
        while (en.MoveNext())
        {
            Media m = en.Value as Media;
            if (m != null) mediaList.Add(m);
        }

        for (int i = 0; i < mediaList.Count; i++)
        {
            Media media = mediaList[i];
            bool offlineBefore;
            try { offlineBefore = media.IsOffline(); }
            catch (Exception ex) { LogDiag("IsOffline() threw for media: " + ex.Message); continue; }

            if (!offlineBefore) continue;

            string originalPath = "";
            try { originalPath = media.FilePath; } catch { }
            LogDiag("OFFLINE media detected: " + originalPath);

            // MediaFile.FilePath's setter is not publicly accessible from script code
            // (reflection shows it exists but with restricted visibility), so relink by
            // adding the correct file as new pool media and replacing all usages of the
            // offline media with it -- both are confirmed-public Media/MediaPool members.
            bool relinkedAny = false;
            try
            {
                string wantName = Path.GetFileName(originalPath);
                if (!string.IsNullOrEmpty(wantName))
                {
                    string match = FindByFileName(allFiles, wantName);
                    if (match != null)
                    {
                        Media replacement = project.MediaPool.AddMedia(match);
                        if (replacement != null)
                        {
                            media.ReplaceWith(replacement);
                            relinkedAny = true;
                            LogDiag("Relinked '" + wantName + "' -> " + match);
                        }
                        else
                        {
                            LogDiag("AddMedia returned null for candidate: " + match);
                        }
                    }
                    else
                    {
                        LogDiag("No candidate found under search roots for: " + wantName);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDiag("Error relinking " + originalPath + ": " + ex.Message);
            }

            if (relinkedAny)
            {
                try { media.RefreshNeeded(); }
                catch (Exception ex) { LogDiag("RefreshNeeded() threw: " + ex.Message); }

                bool offlineAfter = true;
                try { offlineAfter = media.IsOffline(); } catch { }
                LogDiag((offlineAfter ? "STILL OFFLINE after relink attempt: " : "RESOLVED (now online): ") + originalPath);
            }
        }
    }

    static string FindByFileName(List<string> allFiles, string name)
    {
        for (int i = 0; i < allFiles.Count; i++)
        {
            if (string.Equals(Path.GetFileName(allFiles[i]), name, StringComparison.OrdinalIgnoreCase))
                return allFiles[i];
        }
        return null;
    }

    // ---------- Project ----------

    static string ExportProject(Vegas vegas)
    {
        Project project = vegas.Project;
        List<string> props = new List<string>();

        props.Add(Prop("inspectorVersion", JS("1.0")));
        props.Add(Prop("vegasVersion", JS(SafeStr(() => vegas.Version))));
        props.Add(Prop("vegasBuildNumber", SafeNum(() => (double)vegas.BuildNumber)));
        props.Add(Prop("projectFilePath", JS(SafeStr(() => project.FilePath))));
        props.Add(Prop("isUntitled", SafeBool(() => project.IsUntitled)));
        props.Add(Prop("isModified", SafeBool(() => project.IsModified)));
        props.Add(Prop("lengthSeconds", SafeNum(() => SecondsOf(project.Length))));

        // Video properties
        try
        {
            ProjectVideoProperties v = project.Video;
            List<string> vp = new List<string>();
            vp.Add(Prop("width", JI(v.Width)));
            vp.Add(Prop("height", JI(v.Height)));
            vp.Add(Prop("pixelAspectRatio", JN(v.PixelAspectRatio)));
            vp.Add(Prop("frameRate", JN(v.FrameRate)));
            vp.Add(Prop("fieldOrder", JS(v.FieldOrder.ToString())));
            vp.Add(Prop("pixelFormat", JS(v.PixelFormat.ToString())));
            vp.Add(Prop("renderQuality", JS(v.RenderQuality.ToString())));
            vp.Add(Prop("outputRotation", JS(v.OutputRotation.ToString())));
            props.Add(Prop("video", Obj(vp)));
        }
        catch (Exception ex) { LogDiag("Project.Video export failed: " + ex.Message); props.Add(Prop("video", JNull())); }

        // Audio properties
        try
        {
            ProjectAudioProperties a = project.Audio;
            List<string> ap = new List<string>();
            ap.Add(Prop("sampleRate", JI(a.SampleRate)));
            ap.Add(Prop("bitDepth", JI(a.BitDepth)));
            ap.Add(Prop("masterBusMode", JS(a.MasterBusMode.ToString())));
            ap.Add(Prop("resampleQuality", JS(a.ResampleQuality.ToString())));
            props.Add(Prop("audio", Obj(ap)));
        }
        catch (Exception ex) { LogDiag("Project.Audio export failed: " + ex.Message); props.Add(Prop("audio", JNull())); }

        // Ruler (unknown exact member set -> reflect at runtime instead of guessing)
        props.Add(Prop("ruler", ReflectObjectShallow(SafeGetRuler(project), "RulerProperties")));

        props.Add(Prop("renderPath", JS(SafeStr(() => project.RenderPath))));
        props.Add(Prop("defaultRenderPath", JS(SafeStr(() => project.DefaultRenderPath))));

        props.Add(Prop("tracks", ExportTracks(project)));
        props.Add(Prop("markers", ExportMarkers(project)));
        props.Add(Prop("regions", ExportRegions(project)));
        props.Add(Prop("media", ExportMediaPool(project)));

        return Obj(props);
    }

    static object SafeGetRuler(Project project)
    {
        try { return project.Ruler; }
        catch (Exception ex) { LogDiag("Project.Ruler access failed: " + ex.Message); return null; }
    }

    // Generic shallow reflection dump for objects whose member names we didn't confirm ahead of time.
    static string ReflectObjectShallow(object obj, string label)
    {
        if (obj == null) return JNull();
        try
        {
            List<string> props = new List<string>();
            System.Reflection.PropertyInfo[] pis = obj.GetType().GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < pis.Length; i++)
            {
                System.Reflection.PropertyInfo pi = pis[i];
                if (pi.GetIndexParameters().Length > 0) continue;
                if (!pi.CanRead) continue;
                object val;
                try { val = pi.GetValue(obj, null); }
                catch (Exception ex) { LogDiag("Reflect " + label + "." + pi.Name + " threw: " + ex.Message); continue; }
                props.Add(Prop(pi.Name, JS(val == null ? "null" : val.ToString())));
            }
            return Obj(props);
        }
        catch (Exception ex)
        {
            LogDiag("ReflectObjectShallow(" + label + ") failed: " + ex.Message);
            return JNull();
        }
    }

    static string SafeStr(Func<string> f)
    {
        try { return f(); } catch (Exception ex) { LogDiag("SafeStr failed: " + ex.Message); return null; }
    }
    static string SafeBool(Func<bool> f)
    {
        try { return JB(f()); } catch (Exception ex) { LogDiag("SafeBool failed: " + ex.Message); return JNull(); }
    }
    static string SafeNum(Func<double> f)
    {
        try { return JN(f()); } catch (Exception ex) { LogDiag("SafeNum failed: " + ex.Message); return JNull(); }
    }

    // ---------- Tracks ----------

    static string ExportTracks(Project project)
    {
        List<string> tracks = new List<string>();
        Tracks all = project.Tracks;
        for (int i = 0; i < all.Count; i++)
        {
            try
            {
                tracks.Add(ExportTrack(all[i], i));
            }
            catch (Exception ex)
            {
                LogDiag("ExportTrack failed at index " + i + ": " + ex.Message);
            }
        }
        return Arr(tracks);
    }

    static string ExportTrack(Track track, int index)
    {
        List<string> props = new List<string>();
        bool isVideo = track.IsVideo();
        bool isAudio = track.IsAudio();

        props.Add(Prop("index", JI(index)));
        props.Add(Prop("name", JS(SafeStr(() => track.Name))));
        props.Add(Prop("mediaType", JS(track.MediaType.ToString())));
        props.Add(Prop("isVideo", JB(isVideo)));
        props.Add(Prop("isAudio", JB(isAudio)));
        props.Add(Prop("mute", SafeBool(() => track.Mute)));
        props.Add(Prop("solo", SafeBool(() => track.Solo)));

        if (isVideo)
        {
            try
            {
                VideoTrack vt = (VideoTrack)track;
                props.Add(Prop("compositeMode", JS(vt.CompositeMode.ToString())));
                props.Add(Prop("compositeLevel", JN(vt.CompositeLevel)));
                props.Add(Prop("isAdjustmentTrack", JB(vt.IsAdjustmentTrack)));
                props.Add(Prop("isCompositingChild", JB(vt.IsCompositingChild)));
                props.Add(Prop("isCompositingParent", JB(vt.IsCompositingParent)));
                props.Add(Prop("trackMotion", ExportTrackMotion(vt.TrackMotion)));
            }
            catch (Exception ex) { LogDiag("VideoTrack extras failed for track " + index + ": " + ex.Message); }
        }

        if (isAudio)
        {
            try
            {
                AudioTrack at = (AudioTrack)track;
                props.Add(Prop("volume", JN(at.Volume)));
                props.Add(Prop("panX", JN(at.PanX)));
                props.Add(Prop("panY", JN(at.PanY)));
            }
            catch (Exception ex) { LogDiag("AudioTrack extras failed for track " + index + ": " + ex.Message); }
        }

        props.Add(Prop("effects", ExportEffects(SafeGetEffects(track))));
        props.Add(Prop("envelopes", ExportEnvelopes(SafeGetEnvelopes(track))));
        props.Add(Prop("events", ExportEvents(track, index)));

        return Obj(props);
    }

    static Effects SafeGetEffects(Track track)
    {
        try { return track.Effects; } catch (Exception ex) { LogDiag("Track.Effects failed: " + ex.Message); return null; }
    }
    static Envelopes SafeGetEnvelopes(Track track)
    {
        try { return track.Envelopes; } catch (Exception ex) { LogDiag("Track.Envelopes failed: " + ex.Message); return null; }
    }

    static string ExportTrackMotion(TrackMotion tm)
    {
        if (tm == null) return JNull();
        try
        {
            List<string> props = new List<string>();
            props.Add(Prop("hasMotionData", JB(tm.HasMotionData)));
            props.Add(Prop("hasGlowData", JB(tm.HasGlowData)));
            props.Add(Prop("hasShadowData", JB(tm.HasShadowData)));
            List<string> kfs = new List<string>();
            if (tm.HasMotionData)
            {
                TrackMotionKeyframeList list = tm.MotionKeyframes;
                for (int i = 0; i < list.Count; i++)
                {
                    TrackMotionKeyframe k = list[i];
                    List<string> kp = new List<string>();
                    kp.Add(Prop("timeSeconds", TrySeconds(k.Position)));
                    kp.Add(Prop("positionX", JN(k.PositionX)));
                    kp.Add(Prop("positionY", JN(k.PositionY)));
                    kp.Add(Prop("positionZ", JN(k.PositionZ)));
                    kp.Add(Prop("rotationX", JN(k.RotationX)));
                    kp.Add(Prop("rotationY", JN(k.RotationY)));
                    kp.Add(Prop("rotationZ", JN(k.RotationZ)));
                    kp.Add(Prop("width", JN(k.Width)));
                    kp.Add(Prop("height", JN(k.Height)));
                    kp.Add(Prop("smoothness", JN(k.Smoothness)));
                    kp.Add(Prop("type", JS(k.Type.ToString())));
                    kfs.Add(Obj(kp));
                }
            }
            props.Add(Prop("motionKeyframes", Arr(kfs)));
            return Obj(props);
        }
        catch (Exception ex)
        {
            LogDiag("ExportTrackMotion failed: " + ex.Message);
            return JNull();
        }
    }

    // ---------- Events ----------

    static string ExportEvents(Track track, int trackIndex)
    {
        List<string> events = new List<string>();
        TrackEvents all = track.Events;
        for (int i = 0; i < all.Count; i++)
        {
            try
            {
                events.Add(ExportEvent(all[i], trackIndex, i));
            }
            catch (Exception ex)
            {
                LogDiag("ExportEvent failed at track " + trackIndex + " index " + i + ": " + ex.Message);
            }
        }
        return Arr(events);
    }

    static string ExportEvent(TrackEvent ev, int trackIndex, int eventIndex)
    {
        List<string> props = new List<string>();
        bool isVideo = ev.IsVideo();
        bool isAudio = ev.IsAudio();

        string stableId = "t" + trackIndex + "_e" + eventIndex;
        props.Add(Prop("inspectorId", JS(stableId)));
        props.Add(Prop("trackIndex", JI(trackIndex)));
        props.Add(Prop("eventIndex", JI(eventIndex)));
        props.Add(Prop("eventID", SafeNum(() => (double)ev.EventID)));
        props.Add(Prop("name", JS(SafeStr(() => ev.Name))));
        props.Add(Prop("isVideo", JB(isVideo)));
        props.Add(Prop("isAudio", JB(isAudio)));
        props.Add(Prop("startSeconds", TrySeconds(ev.Start)));
        props.Add(Prop("endSeconds", TrySeconds(ev.End)));
        props.Add(Prop("lengthSeconds", TrySeconds(ev.Length)));
        props.Add(Prop("playbackRate", SafeNum(() => ev.PlaybackRate)));
        props.Add(Prop("loop", SafeBool(() => ev.Loop)));
        props.Add(Prop("locked", SafeBool(() => ev.Locked)));
        props.Add(Prop("mute", SafeBool(() => ev.Mute)));
        props.Add(Prop("selected", SafeBool(() => ev.Selected)));
        props.Add(Prop("isGrouped", SafeBool(() => ev.IsGrouped)));

        try
        {
            if (ev.IsGrouped && ev.Group != null)
            {
                List<string> ids = new List<string>();
                TrackEventGroup g = ev.Group;
                for (int i = 0; i < g.Count; i++)
                {
                    try { ids.Add(JI(g[i].EventID)); } catch { }
                }
                props.Add(Prop("groupEventIDs", Arr(ids)));
            }
            else
            {
                props.Add(Prop("groupEventIDs", JNull()));
            }
        }
        catch (Exception ex) { LogDiag("Group export failed for " + stableId + ": " + ex.Message); }

        props.Add(Prop("fadeIn", ExportFade(SafeGetFadeIn(ev))));
        props.Add(Prop("fadeOut", ExportFade(SafeGetFadeOut(ev))));

        List<string> takes = new List<string>();
        try
        {
            Takes tks = ev.Takes;
            for (int i = 0; i < tks.Count; i++)
            {
                takes.Add(ExportTake(tks[i]));
            }
        }
        catch (Exception ex) { LogDiag("Takes export failed for " + stableId + ": " + ex.Message); }
        props.Add(Prop("takes", Arr(takes)));

        try { props.Add(Prop("activeTakeIndex", JI(ev.ActiveTake != null ? ev.ActiveTake.Index : -1))); }
        catch (Exception ex) { LogDiag("ActiveTake index failed for " + stableId + ": " + ex.Message); props.Add(Prop("activeTakeIndex", JNull())); }

        if (isVideo)
        {
            try
            {
                VideoEvent vev = (VideoEvent)ev;
                props.Add(Prop("maintainAspectRatio", JB(vev.MaintainAspectRatio)));
                props.Add(Prop("resampleMode", JS(vev.ResampleMode.ToString())));
                props.Add(Prop("effects", ExportEffects(SafeGetEffectsV(vev))));
                props.Add(Prop("envelopes", ExportEnvelopes(SafeGetEnvelopesV(vev))));
                props.Add(Prop("videoMotion", ExportVideoMotion(vev)));
            }
            catch (Exception ex) { LogDiag("VideoEvent extras failed for " + stableId + ": " + ex.Message); }
        }
        else if (isAudio)
        {
            try
            {
                AudioEvent aev = (AudioEvent)ev;
                props.Add(Prop("normalize", JB(aev.Normalize)));
                props.Add(Prop("normalizeGain", JN(aev.NormalizeGain)));
                props.Add(Prop("invertPhase", JB(aev.InvertPhase)));
                props.Add(Prop("effects", ExportEffects(SafeGetEffectsA(aev))));
            }
            catch (Exception ex) { LogDiag("AudioEvent extras failed for " + stableId + ": " + ex.Message); }
        }

        return Obj(props);
    }

    static Effects SafeGetEffectsV(VideoEvent v) { try { return v.Effects; } catch (Exception ex) { LogDiag("VideoEvent.Effects failed: " + ex.Message); return null; } }
    static Envelopes SafeGetEnvelopesV(VideoEvent v) { try { return v.Envelopes; } catch (Exception ex) { LogDiag("VideoEvent.Envelopes failed: " + ex.Message); return null; } }
    static Effects SafeGetEffectsA(AudioEvent a) { try { return a.Effects; } catch (Exception ex) { LogDiag("AudioEvent.Effects failed: " + ex.Message); return null; } }
    static Fade SafeGetFadeIn(TrackEvent ev) { try { return ev.FadeIn; } catch (Exception ex) { LogDiag("FadeIn failed: " + ex.Message); return null; } }
    static Fade SafeGetFadeOut(TrackEvent ev) { try { return ev.FadeOut; } catch (Exception ex) { LogDiag("FadeOut failed: " + ex.Message); return null; } }

    static string ExportFade(Fade fade)
    {
        if (fade == null) return JNull();
        try
        {
            List<string> props = new List<string>();
            props.Add(Prop("lengthSeconds", TrySeconds(fade.Length)));
            props.Add(Prop("curve", JS(fade.Curve.ToString())));
            props.Add(Prop("gain", JN(fade.Gain)));
            return Obj(props);
        }
        catch (Exception ex) { LogDiag("ExportFade failed: " + ex.Message); return JNull(); }
    }

    static string ExportTake(Take take)
    {
        List<string> props = new List<string>();
        try
        {
            props.Add(Prop("index", JI(take.Index)));
            props.Add(Prop("name", JS(SafeStr(() => take.Name))));
            props.Add(Prop("isActive", JB(take.IsActive)));
            props.Add(Prop("offsetSeconds", TrySeconds(take.Offset)));
            props.Add(Prop("lengthSeconds", TrySeconds(take.Length)));
            props.Add(Prop("availableLengthSeconds", TrySeconds(take.AvailableLength)));
            props.Add(Prop("mediaPath", JS(SafeStr(() => take.MediaPath))));
            try
            {
                Media m = take.Media;
                props.Add(Prop("mediaOffline", m != null ? JB(m.IsOffline()) : JNull()));
            }
            catch (Exception ex) { LogDiag("Take media offline check failed: " + ex.Message); props.Add(Prop("mediaOffline", JNull())); }
        }
        catch (Exception ex)
        {
            LogDiag("ExportTake failed: " + ex.Message);
        }
        return Obj(props);
    }

    // ---------- VideoMotion (Pan/Crop) ----------

    static string ExportVideoMotion(VideoEvent vev)
    {
        try
        {
            VideoMotion vm = vev.VideoMotion;
            if (vm == null) return JNull();
            VideoMotionKeyframes kfs = vm.Keyframes;
            List<string> list = new List<string>();

            double baselineWidth = 0, baselineHeight = 0;
            bool haveBaseline = false;

            for (int i = 0; i < kfs.Count; i++)
            {
                VideoMotionKeyframe k = kfs[i];
                List<string> kp = new List<string>();
                kp.Add(Prop("index", JI(i)));
                kp.Add(Prop("eventRelativeSeconds", TrySeconds(k.Position)));
                kp.Add(Prop("timelineAbsoluteSeconds", JN(SecondsOf(vev.Start) + SecondsOf(k.Position))));
                kp.Add(Prop("rotation", JN(k.Rotation)));
                kp.Add(Prop("smoothness", JN(k.Smoothness)));
                kp.Add(Prop("type", JS(k.Type.ToString())));
                kp.Add(Prop("isValid", JB(k.IsValid())));

                VideoMotionBounds b = k.Bounds;
                List<string> bp = new List<string>();
                bp.Add(Prop("topLeft", VertexObj(b.TopLeft)));
                bp.Add(Prop("topRight", VertexObj(b.TopRight)));
                bp.Add(Prop("bottomLeft", VertexObj(b.BottomLeft)));
                bp.Add(Prop("bottomRight", VertexObj(b.BottomRight)));
                kp.Add(Prop("bounds", Obj(bp)));

                double width = Math.Abs(b.TopRight.X - b.TopLeft.X);
                double height = Math.Abs(b.BottomLeft.Y - b.TopLeft.Y);
                kp.Add(Prop("boundsWidth", JN(width)));
                kp.Add(Prop("boundsHeight", JN(height)));

                if (!haveBaseline) { baselineWidth = width; baselineHeight = height; haveBaseline = true; }
                double zoomW = width > 0.0001 ? baselineWidth / width : 1.0;
                double zoomH = height > 0.0001 ? baselineHeight / height : 1.0;
                kp.Add(Prop("inferredZoomRelativeToFirstKeyframe", JN((zoomW + zoomH) / 2.0)));

                List<string> cp = new List<string>();
                cp.Add(Prop("x", JN(k.Center.X)));
                cp.Add(Prop("y", JN(k.Center.Y)));
                kp.Add(Prop("center", Obj(cp)));

                list.Add(Obj(kp));
            }

            List<string> outer = new List<string>();
            outer.Add(Prop("scaleToFill", SafeBool(() => vm.ScaleToFill)));
            outer.Add(Prop("keyframeCount", JI(kfs.Count)));
            outer.Add(Prop("keyframes", Arr(list)));

            bool returnsToBaseline = false;
            if (kfs.Count >= 2)
            {
                try
                {
                    VideoMotionBounds first = kfs[0].Bounds;
                    VideoMotionBounds last = kfs[kfs.Count - 1].Bounds;
                    double tol = 0.5;
                    returnsToBaseline =
                        Math.Abs(first.TopLeft.X - last.TopLeft.X) < tol &&
                        Math.Abs(first.TopLeft.Y - last.TopLeft.Y) < tol &&
                        Math.Abs(first.BottomRight.X - last.BottomRight.X) < tol &&
                        Math.Abs(first.BottomRight.Y - last.BottomRight.Y) < tol;
                }
                catch (Exception ex) { LogDiag("returnsToBaseline compare failed: " + ex.Message); }
            }
            else if (kfs.Count <= 1)
            {
                returnsToBaseline = true;
            }
            outer.Add(Prop("returnsToBaseline", JB(returnsToBaseline)));

            return Obj(outer);
        }
        catch (Exception ex)
        {
            LogDiag("ExportVideoMotion failed: " + ex.Message);
            return JNull();
        }
    }

    static string VertexObj(VideoMotionVertex v)
    {
        List<string> p = new List<string>();
        p.Add(Prop("x", JN(v.X)));
        p.Add(Prop("y", JN(v.Y)));
        return Obj(p);
    }

    // ---------- Effects ----------

    static string ExportEffects(Effects effects)
    {
        List<string> list = new List<string>();
        if (effects == null) return Arr(list);
        for (int i = 0; i < effects.Count; i++)
        {
            try { list.Add(ExportEffect(effects[i], i)); }
            catch (Exception ex) { LogDiag("ExportEffect failed at index " + i + ": " + ex.Message); }
        }
        return Arr(list);
    }

    static string ExportEffect(Effect fx, int index)
    {
        List<string> props = new List<string>();
        props.Add(Prop("index", JI(index)));
        props.Add(Prop("bypass", SafeBool(() => fx.Bypass)));
        props.Add(Prop("isOFX", SafeBool(() => fx.IsOFX)));
        props.Add(Prop("isValid", SafeBool(() => fx.IsValid())));
        props.Add(Prop("description", JS(SafeStr(() => fx.Description))));

        try
        {
            PlugInNode plugin = fx.PlugIn;
            if (plugin != null)
            {
                List<string> pp = new List<string>();
                pp.Add(Prop("name", JS(SafeStr(() => plugin.Name))));
                pp.Add(Prop("uniqueID", JS(SafeStr(() => plugin.UniqueID))));
                pp.Add(Prop("classID", JS(SafeStr(() => plugin.ClassID.ToString()))));
                pp.Add(Prop("group", JS(SafeStr(() => plugin.Group))));
                pp.Add(Prop("isOFX", SafeBool(() => plugin.IsOFX)));
                pp.Add(Prop("isDisabled", SafeBool(() => plugin.IsDisabled)));
                props.Add(Prop("plugin", Obj(pp)));
                props.Add(Prop("pluginAvailable", JB(!plugin.IsDisabled)));
            }
            else
            {
                props.Add(Prop("plugin", JNull()));
                props.Add(Prop("pluginAvailable", JB(false)));
                LogDiag("Effect has null PlugIn (likely missing plug-in): " + SafeStr(() => fx.Description));
            }
        }
        catch (Exception ex)
        {
            LogDiag("Effect.PlugIn access failed: " + ex.Message);
            props.Add(Prop("plugin", JNull()));
            props.Add(Prop("pluginAvailable", JB(false)));
        }

        try
        {
            EffectPreset preset = fx.CurrentPreset;
            props.Add(Prop("currentPresetName", preset != null ? JS(preset.Name) : JNull()));
        }
        catch (Exception ex) { LogDiag("Effect.CurrentPreset failed: " + ex.Message); props.Add(Prop("currentPresetName", JNull())); }

        try
        {
            if (fx.IsOFX && fx.OFXEffect != null)
            {
                OFXEffect ofx = fx.OFXEffect;
                List<string> paramList = new List<string>();
                OFXParameters parameters = ofx.Parameters;
                for (int p = 0; p < parameters.Count; p++)
                {
                    try { paramList.Add(ExportOFXParameter(parameters[p])); }
                    catch (Exception ex) { LogDiag("ExportOFXParameter failed at index " + p + ": " + ex.Message); }
                }
                props.Add(Prop("ofxParameters", Arr(paramList)));
            }
        }
        catch (Exception ex)
        {
            LogDiag("OFX parameter export failed: " + ex.Message);
        }

        return Obj(props);
    }

    // Confirmed via targeted reflection of the OFXParameter`2/OFXKeyframe`1 family
    // (base OFXParameter alone doesn't expose Value/Keyframes -- those only exist
    // on the concrete typed subclasses, so we must switch on ParameterType and cast).
    static string ExportOFXParameter(OFXParameter param)
    {
        List<string> props = new List<string>();
        props.Add(Prop("name", JS(SafeStr(() => param.Name))));
        props.Add(Prop("label", JS(SafeStr(() => param.Label))));
        props.Add(Prop("parameterType", JS(param.ParameterType.ToString())));
        props.Add(Prop("canAnimate", JB(param.CanAnimate)));
        props.Add(Prop("isAnimated", JB(param.IsAnimated)));
        props.Add(Prop("enabled", JB(param.Enabled)));

        try
        {
            switch (param.ParameterType)
            {
                case OFXParameterType.Double:
                    {
                        OFXDoubleParameter p = (OFXDoubleParameter)param;
                        props.Add(Prop("value", JN(p.Value)));
                        props.Add(Prop("default", JN(p.Default)));
                        props.Add(Prop("min", JN(p.Min)));
                        props.Add(Prop("max", JN(p.Max)));
                        List<string> kfs = new List<string>();
                        for (int i = 0; i < p.Keyframes.Count; i++)
                        {
                            OFXDoubleKeyframe k = p.Keyframes[i];
                            List<string> kp = new List<string>();
                            kp.Add(Prop("timeSeconds", TrySeconds(k.Time)));
                            kp.Add(Prop("value", JN(k.Value)));
                            kp.Add(Prop("interpolation", JS(k.Interpolation.ToString())));
                            kfs.Add(Obj(kp));
                        }
                        props.Add(Prop("keyframes", Arr(kfs)));
                        break;
                    }
                case OFXParameterType.Integer:
                    {
                        OFXIntegerParameter p = (OFXIntegerParameter)param;
                        props.Add(Prop("value", JI(p.Value)));
                        props.Add(Prop("default", JI(p.Default)));
                        List<string> kfs = new List<string>();
                        for (int i = 0; i < p.Keyframes.Count; i++)
                        {
                            OFXIntegerKeyframe k = p.Keyframes[i];
                            List<string> kp = new List<string>();
                            kp.Add(Prop("timeSeconds", TrySeconds(k.Time)));
                            kp.Add(Prop("value", JI(k.Value)));
                            kp.Add(Prop("interpolation", JS(k.Interpolation.ToString())));
                            kfs.Add(Obj(kp));
                        }
                        props.Add(Prop("keyframes", Arr(kfs)));
                        break;
                    }
                case OFXParameterType.Boolean:
                    {
                        OFXBooleanParameter p = (OFXBooleanParameter)param;
                        props.Add(Prop("value", JB(p.Value)));
                        props.Add(Prop("default", JB(p.Default)));
                        List<string> kfs = new List<string>();
                        for (int i = 0; i < p.Keyframes.Count; i++)
                        {
                            OFXBooleanKeyframe k = p.Keyframes[i];
                            List<string> kp = new List<string>();
                            kp.Add(Prop("timeSeconds", TrySeconds(k.Time)));
                            kp.Add(Prop("value", JB(k.Value)));
                            kp.Add(Prop("interpolation", JS(k.Interpolation.ToString())));
                            kfs.Add(Obj(kp));
                        }
                        props.Add(Prop("keyframes", Arr(kfs)));
                        break;
                    }
                case OFXParameterType.Choice:
                    {
                        OFXChoiceParameter p = (OFXChoiceParameter)param;
                        props.Add(Prop("value", JS(SafeStr(() => p.Value.ToString()))));
                        List<string> kfs = new List<string>();
                        for (int i = 0; i < p.Keyframes.Count; i++)
                        {
                            OFXChoiceKeyframe k = p.Keyframes[i];
                            List<string> kp = new List<string>();
                            kp.Add(Prop("timeSeconds", TrySeconds(k.Time)));
                            kp.Add(Prop("value", JS(SafeStr(() => k.Value.ToString()))));
                            kp.Add(Prop("interpolation", JS(k.Interpolation.ToString())));
                            kfs.Add(Obj(kp));
                        }
                        props.Add(Prop("keyframes", Arr(kfs)));
                        break;
                    }
                default:
                    // Double2D/3D, RGB(A), Integer2D/3D, String, Custom: not needed for
                    // this montage's effect set (shake/flicker/blur/glow intensities are
                    // plain Double sliders); record only that they exist.
                    props.Add(Prop("keyframeCountUnparsed", JNull()));
                    break;
            }
        }
        catch (Exception ex)
        {
            LogDiag("Typed OFX parameter export failed for " + SafeStr(() => param.Name) + ": " + ex.Message);
        }

        return Obj(props);
    }

    // ---------- Envelopes ----------

    static string ExportEnvelopes(Envelopes envs)
    {
        List<string> list = new List<string>();
        if (envs == null) return Arr(list);
        for (int i = 0; i < envs.Count; i++)
        {
            try { list.Add(ExportEnvelope(envs[i])); }
            catch (Exception ex) { LogDiag("ExportEnvelope failed at index " + i + ": " + ex.Message); }
        }
        return Arr(list);
    }

    static string ExportEnvelope(Envelope env)
    {
        List<string> props = new List<string>();
        props.Add(Prop("type", JS(env.Type.ToString())));
        props.Add(Prop("min", JN(env.Min)));
        props.Add(Prop("max", JN(env.Max)));
        props.Add(Prop("neutral", JN(env.Neutral)));

        List<string> points = new List<string>();
        EnvelopePoints pts = env.Points;
        for (int i = 0; i < pts.Count; i++)
        {
            EnvelopePoint pt = pts[i];
            List<string> pp = new List<string>();
            pp.Add(Prop("timeSeconds", TrySeconds(pt.X)));
            pp.Add(Prop("value", JN(pt.Y)));
            pp.Add(Prop("curve", JS(pt.Curve.ToString())));
            points.Add(Obj(pp));
        }
        props.Add(Prop("points", Arr(points)));
        return Obj(props);
    }

    // ---------- Markers / Regions ----------

    static string ExportMarkers(Project project)
    {
        List<string> list = new List<string>();
        MarkerList markers = project.Markers;
        for (int i = 0; i < markers.Count; i++)
        {
            try
            {
                Marker m = markers[i];
                List<string> props = new List<string>();
                props.Add(Prop("index", JI(i)));
                props.Add(Prop("label", JS(m.Label)));
                props.Add(Prop("timeSeconds", TrySeconds(m.Position)));
                list.Add(Obj(props));
            }
            catch (Exception ex) { LogDiag("Marker export failed at index " + i + ": " + ex.Message); }
        }
        return Arr(list);
    }

    static string ExportRegions(Project project)
    {
        List<string> list = new List<string>();
        RegionList regions = project.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            try
            {
                Region r = regions[i];
                List<string> props = new List<string>();
                props.Add(Prop("index", JI(i)));
                props.Add(Prop("label", JS(r.Label)));
                props.Add(Prop("startSeconds", TrySeconds(r.Position)));
                props.Add(Prop("endSeconds", TrySeconds(r.End)));
                props.Add(Prop("lengthSeconds", TrySeconds(r.Length)));
                list.Add(Obj(props));
            }
            catch (Exception ex) { LogDiag("Region export failed at index " + i + ": " + ex.Message); }
        }
        return Arr(list);
    }

    // ---------- Media pool ----------

    static string ExportMediaPool(Project project)
    {
        List<string> list = new List<string>();
        MediaPool pool = project.MediaPool;
        System.Collections.IDictionaryEnumerator en = pool.GetEnumerator();
        int idx = 0;
        while (en.MoveNext())
        {
            Media m = en.Value as Media;
            if (m == null) continue;
            try
            {
                list.Add(ExportMedia(m, idx));
            }
            catch (Exception ex)
            {
                LogDiag("ExportMedia failed at index " + idx + ": " + ex.Message);
            }
            idx++;
        }
        return Arr(list);
    }

    static string ExportMedia(Media m, int index)
    {
        List<string> props = new List<string>();
        props.Add(Prop("index", JI(index)));
        props.Add(Prop("filePath", JS(SafeStr(() => m.FilePath))));
        props.Add(Prop("mediaID", SafeNum(() => (double)m.MediaID)));
        props.Add(Prop("isOffline", SafeBool(() => m.IsOffline())));
        props.Add(Prop("hasVideo", SafeBool(() => m.HasVideo())));
        props.Add(Prop("hasAudio", SafeBool(() => m.HasAudio())));
        props.Add(Prop("isGenerated", SafeBool(() => m.IsGenerated())));
        props.Add(Prop("isImageSequence", SafeBool(() => m.IsImageSequence())));
        props.Add(Prop("isSubclip", SafeBool(() => m.IsSubclip())));
        props.Add(Prop("useCount", SafeNum(() => (double)m.UseCount)));

        try
        {
            List<string> mfList = new List<string>();
            MediaFiles files = m.MediaFiles;
            for (int i = 0; i < files.Count; i++)
            {
                MediaFile mf = files[i];
                List<string> mfp = new List<string>();
                mfp.Add(Prop("filePath", JS(mf.FilePath)));
                mfp.Add(Prop("type", JS(mf.Type.ToString())));
                mfp.Add(Prop("isEssence", JB(mf.IsEssence)));
                mfp.Add(Prop("isProxy", JB(mf.IsProxy)));
                mfList.Add(Obj(mfp));
            }
            props.Add(Prop("mediaFiles", Arr(mfList)));
        }
        catch (Exception ex) { LogDiag("MediaFiles export failed: " + ex.Message); props.Add(Prop("mediaFiles", Arr(new List<string>()))); }

        try
        {
            if (m.HasVideo())
            {
                VideoStream vs = m.GetVideoStreamByIndex(0);
                if (vs != null)
                {
                    List<string> vsp = new List<string>();
                    vsp.Add(Prop("width", JI(vs.Width)));
                    vsp.Add(Prop("height", JI(vs.Height)));
                    vsp.Add(Prop("frameRate", JN(vs.FrameRate)));
                    vsp.Add(Prop("pixelAspectRatio", JN(vs.PixelAspectRatio)));
                    vsp.Add(Prop("isOffline", JB(vs.IsOffline)));
                    vsp.Add(Prop("lengthSeconds", TrySeconds(vs.Length)));
                    props.Add(Prop("videoStream", Obj(vsp)));
                }
            }
        }
        catch (Exception ex) { LogDiag("VideoStream export failed for media " + index + ": " + ex.Message); }

        try
        {
            if (m.HasAudio())
            {
                AudioStream aus = m.GetAudioStreamByIndex(0);
                if (aus != null)
                {
                    List<string> asp = new List<string>();
                    asp.Add(Prop("channels", JI(aus.Channels)));
                    asp.Add(Prop("sampleRate", JI(aus.SampleRate)));
                    asp.Add(Prop("bitDepth", JI(aus.BitDepth)));
                    asp.Add(Prop("isOffline", JB(aus.IsOffline)));
                    asp.Add(Prop("lengthSeconds", TrySeconds(aus.Length)));
                    props.Add(Prop("audioStream", Obj(asp)));
                }
            }
        }
        catch (Exception ex) { LogDiag("AudioStream export failed for media " + index + ": " + ex.Message); }

        try
        {
            List<string> mm = new List<string>();
            MediaMarkerList markers = m.Markers;
            for (int i = 0; i < markers.Count; i++)
            {
                List<string> mp = new List<string>();
                mp.Add(Prop("label", JS(markers[i].Label)));
                mp.Add(Prop("timeSeconds", TrySeconds(markers[i].Position)));
                mm.Add(Obj(mp));
            }
            props.Add(Prop("mediaMarkers", Arr(mm)));
        }
        catch (Exception ex) { LogDiag("Media.Markers export failed: " + ex.Message); }

        return Obj(props);
    }
}

public delegate T Func<T>();
