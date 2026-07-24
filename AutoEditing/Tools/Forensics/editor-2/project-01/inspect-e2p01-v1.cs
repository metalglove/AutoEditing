using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;

public class EntryPoint
{
    static List<string> Diag = new List<string>();

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
            VerifyMediaOnlineStatus(vegas.Project);

            string projectJson = ExportProject(vegas);

            string outPath = @"C:\VEGAS\editor-2-project-01-analysis\raw\project-inspection-v1.json";
            File.WriteAllText(outPath, projectJson, new UTF8Encoding(false));

            string diagPath = @"C:\VEGAS\editor-2-project-01-analysis\diagnostics\project-inspection-diagnostics-raw.txt";
            File.WriteAllLines(diagPath, Diag.ToArray());
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    @"C:\VEGAS\editor-2-project-01-analysis\diagnostics\project-inspection-FATAL.txt",
                    ex.ToString());
            }
            catch { }
        }
    }

    // ---------- Media state verification (read-only) ----------

    static void VerifyMediaOnlineStatus(Project project)
    {
        MediaPool pool = project.MediaPool;
        System.Collections.IDictionaryEnumerator en = pool.GetEnumerator();
        int total = 0, offline = 0;
        while (en.MoveNext())
        {
            Media m = en.Value as Media;
            if (m == null) continue;
            total++;
            bool isOff;
            try { isOff = m.IsOffline(); } catch (Exception ex) { LogDiag("VerifyMedia: IsOffline() threw for media " + total + ": " + ex.Message); continue; }
            if (isOff)
            {
                offline++;
                string p = "";
                try { p = m.FilePath; } catch { }
                LogDiag("VerifyMedia: OFFLINE: " + p);
            }
        }
        LogDiag("VerifyMedia: total=" + total + " offline=" + offline);
    }

    // ---------- Project ----------

    static string ExportProject(Vegas vegas)
    {
        Project project = vegas.Project;
        List<string> props = new List<string>();

        props.Add(Prop("schemaVersion", JS("e2p01-1.0")));
        props.Add(Prop("inspectorVersion", JS("3.0-reused-from-editor1")));
        props.Add(Prop("vegasVersion", JS(SafeStr(() => vegas.Version))));
        props.Add(Prop("vegasBuildNumber", SafeNum(() => (double)vegas.BuildNumber)));
        props.Add(Prop("projectFilePath", JS(SafeStr(() => project.FilePath))));
        props.Add(Prop("isUntitled", SafeBool(() => project.IsUntitled)));
        props.Add(Prop("isModified", SafeBool(() => project.IsModified)));
        props.Add(Prop("lengthSeconds", SafeNum(() => SecondsOf(project.Length))));

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

        props.Add(Prop("ruler", ReflectObjectShallow(SafeGetRuler(project), "RulerProperties")));

        props.Add(Prop("renderPath", JS(SafeStr(() => project.RenderPath))));
        props.Add(Prop("defaultRenderPath", JS(SafeStr(() => project.DefaultRenderPath))));

        try
        {
            AudioBusTrack mb = project.MasterBus;
            List<string> mbp = new List<string>();
            mbp.Add(Prop("name", JS(SafeStr(() => mb.Name))));
            mbp.Add(Prop("mute", SafeBool(() => mb.Mute)));
            mbp.Add(Prop("effects", ExportEffects(mb.Effects, false, 0)));
            mbp.Add(Prop("envelopes", ExportEnvelopes(mb.Envelopes)));
            props.Add(Prop("masterBus", Obj(mbp)));
        }
        catch (Exception ex) { LogDiag("Project.MasterBus export failed: " + ex.Message); props.Add(Prop("masterBus", JNull())); }

        try
        {
            VideoBusTrack vb = project.VideoBus;
            List<string> vbp = new List<string>();
            vbp.Add(Prop("name", JS(SafeStr(() => vb.Name))));
            vbp.Add(Prop("effects", ExportEffects(vb.Effects, false, 0)));
            vbp.Add(Prop("envelopes", ExportEnvelopes(vb.Envelopes)));
            props.Add(Prop("videoBus", Obj(vbp)));
        }
        catch (Exception ex) { LogDiag("Project.VideoBus export failed: " + ex.Message); props.Add(Prop("videoBus", JNull())); }

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

        props.Add(Prop("effects", ExportEffects(SafeGetEffects(track), false, 0)));
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
        props.Add(Prop("eventID", JS(SafeStr(() => ev.EventID.ToString(CultureInfo.InvariantCulture)))));
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
                    try { ids.Add(JS(g[i].EventID.ToString(CultureInfo.InvariantCulture))); } catch { }
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
                props.Add(Prop("effects", ExportEffects(SafeGetEffectsV(vev), true, SecondsOf(ev.Start))));
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
                props.Add(Prop("effects", ExportEffects(SafeGetEffectsA(aev), true, SecondsOf(ev.Start))));
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

    static string ExportEffects(Effects effects, bool hasEventContext, double eventStartSeconds)
    {
        List<string> list = new List<string>();
        if (effects == null) return Arr(list);
        for (int i = 0; i < effects.Count; i++)
        {
            try { list.Add(ExportEffect(effects[i], i, hasEventContext, eventStartSeconds)); }
            catch (Exception ex) { LogDiag("ExportEffect failed at index " + i + ": " + ex.Message); }
        }
        return Arr(list);
    }

    static string ExportEffect(Effect fx, int index, bool hasEventContext, double eventStartSeconds)
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
                string pluginUniqueId = null;
                try { pluginUniqueId = fx.PlugIn != null ? fx.PlugIn.UniqueID : null; } catch { }
                string fxDescription = SafeStr(() => fx.Description);

                List<string> paramList = new List<string>();
                OFXParameters parameters = ofx.Parameters;
                for (int p = 0; p < parameters.Count; p++)
                {
                    try
                    {
                        paramList.Add(ExportOFXParameter(
                            parameters[p], index, pluginUniqueId, fxDescription,
                            hasEventContext, eventStartSeconds));
                    }
                    catch (Exception ex) { LogDiag("ExportOFXParameter failed at index " + p + ": " + ex.Message); }
                }
                props.Add(Prop("ofxParameters", Arr(paramList)));
            }
        }
        catch (Exception ex)
        {
            LogDiag("OFX parameter export failed: " + ex.Message);
        }

        try
        {
            Keyframes classicKfs = fx.Keyframes;
            if (classicKfs != null && classicKfs.Count > 0)
            {
                List<string> ckfList = new List<string>();
                for (int i = 0; i < classicKfs.Count; i++)
                {
                    Keyframe k = classicKfs[i];
                    List<string> ckp = new List<string>();
                    ckp.Add(Prop("index", JI(i)));
                    ckp.Add(Prop("positionSeconds", TrySeconds(k.Position)));
                    ckp.Add(Prop("type", JS(k.Type.ToString())));
                    ckp.Add(Prop("isValid", JB(k.IsValid())));
                    ckfList.Add(Obj(ckp));
                }
                props.Add(Prop("classicKeyframes", Arr(ckfList)));
            }
        }
        catch (Exception ex)
        {
            LogDiag("Classic Keyframes export failed: " + ex.Message);
        }

        return Obj(props);
    }

    private struct RawKf { public double t; public double v; public string interp; }

    static string ExportOFXParameter(
        OFXParameter param, int effectChainIndex, string pluginUniqueId, string effectDescription,
        bool hasEventContext, double eventStartSeconds)
    {
        List<string> props = new List<string>();
        props.Add(Prop("effectChainIndex", JI(effectChainIndex)));
        props.Add(Prop("pluginUniqueId", JS(pluginUniqueId)));
        props.Add(Prop("effectDescription", JS(effectDescription)));
        props.Add(Prop("name", JS(SafeStr(() => param.Name))));
        props.Add(Prop("label", JS(SafeStr(() => param.Label))));
        props.Add(Prop("stableId", JS((pluginUniqueId ?? effectDescription ?? "fx" + effectChainIndex) + "::" + SafeStr(() => param.Name))));
        props.Add(Prop("parameterType", JS(param.ParameterType.ToString())));
        props.Add(Prop("canAnimate", JB(param.CanAnimate)));
        props.Add(Prop("isAnimated", JB(param.IsAnimated)));
        props.Add(Prop("enabled", JB(param.Enabled)));

        try
        {
            List<RawKf> raw = new List<RawKf>();
            double defaultValue = 0;
            bool haveDefault = false;

            switch (param.ParameterType)
            {
                case OFXParameterType.Double:
                    {
                        OFXDoubleParameter p = (OFXDoubleParameter)param;
                        props.Add(Prop("value", JN(p.Value)));
                        props.Add(Prop("default", JN(p.Default)));
                        props.Add(Prop("min", JN(p.Min)));
                        props.Add(Prop("max", JN(p.Max)));
                        defaultValue = p.Default; haveDefault = true;
                        for (int i = 0; i < p.Keyframes.Count; i++)
                        {
                            OFXDoubleKeyframe k = p.Keyframes[i];
                            RawKf r; r.t = SecondsOf(k.Time); r.v = k.Value; r.interp = k.Interpolation.ToString();
                            raw.Add(r);
                        }
                        break;
                    }
                case OFXParameterType.Integer:
                    {
                        OFXIntegerParameter p = (OFXIntegerParameter)param;
                        props.Add(Prop("value", JI(p.Value)));
                        props.Add(Prop("default", JI(p.Default)));
                        defaultValue = p.Default; haveDefault = true;
                        for (int i = 0; i < p.Keyframes.Count; i++)
                        {
                            OFXIntegerKeyframe k = p.Keyframes[i];
                            RawKf r; r.t = SecondsOf(k.Time); r.v = k.Value; r.interp = k.Interpolation.ToString();
                            raw.Add(r);
                        }
                        break;
                    }
                case OFXParameterType.Boolean:
                    {
                        OFXBooleanParameter p = (OFXBooleanParameter)param;
                        props.Add(Prop("value", JB(p.Value)));
                        props.Add(Prop("default", JB(p.Default)));
                        defaultValue = p.Default ? 1 : 0; haveDefault = true;
                        for (int i = 0; i < p.Keyframes.Count; i++)
                        {
                            OFXBooleanKeyframe k = p.Keyframes[i];
                            RawKf r; r.t = SecondsOf(k.Time); r.v = k.Value ? 1 : 0; r.interp = k.Interpolation.ToString();
                            raw.Add(r);
                        }
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
                            kp.Add(Prop("eventRelativeOrRawSeconds", TrySeconds(k.Time)));
                            kp.Add(Prop("value", JS(SafeStr(() => k.Value.ToString()))));
                            kp.Add(Prop("interpolation", JS(k.Interpolation.ToString())));
                            kfs.Add(Obj(kp));
                        }
                        props.Add(Prop("keyframes", Arr(kfs)));
                        break;
                    }
                default:
                    props.Add(Prop("keyframeCountUnparsed", JNull()));
                    break;
            }

            if (raw.Count > 0)
            {
                List<string> kfs = new List<string>();
                for (int i = 0; i < raw.Count; i++)
                {
                    RawKf r = raw[i];
                    List<string> kp = new List<string>();
                    kp.Add(Prop("index", JI(i)));
                    kp.Add(Prop("eventRelativeOrRawSeconds", JN(r.t)));
                    if (hasEventContext)
                        kp.Add(Prop("assumedTimelineAbsoluteSeconds", JN(eventStartSeconds + r.t)));
                    kp.Add(Prop("value", JN(r.v)));
                    kp.Add(Prop("interpolation", JS(r.interp)));
                    if (haveDefault)
                        kp.Add(Prop("equalsDefault", JB(Math.Abs(r.v - defaultValue) < 0.0005)));
                    kp.Add(Prop("prevValue", i > 0 ? JN(raw[i - 1].v) : JNull()));
                    kp.Add(Prop("nextValue", i < raw.Count - 1 ? JN(raw[i + 1].v) : JNull()));
                    kfs.Add(Obj(kp));
                }
                props.Add(Prop("keyframes", Arr(kfs)));
                if (haveDefault)
                {
                    bool returnsToBaseline = Math.Abs(raw[raw.Count - 1].v - defaultValue) < 0.0005;
                    props.Add(Prop("returnsToDefaultAtLastKeyframe", JB(returnsToBaseline)));
                }
            }
            else if (param.ParameterType == OFXParameterType.Double ||
                     param.ParameterType == OFXParameterType.Integer ||
                     param.ParameterType == OFXParameterType.Boolean)
            {
                props.Add(Prop("keyframes", Arr(new List<string>())));
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
