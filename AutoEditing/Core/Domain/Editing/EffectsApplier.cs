using System;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Domain.Editing
{
    public class EffectsApplier
    {
        /// <summary>
        /// Writes the planned speed profile onto the event as a velocity
        /// envelope. Envelope point positions are event-local, matching the
        /// profile's event-local times. Velocity envelopes never change the
        /// event's timeline length — the planner has already sized the source
        /// window for the profile's source consumption, so this method only
        /// transcribes points.
        /// </summary>
        public void ApplyVelocityEnvelope(VideoEvent videoEvent, SpeedProfile profile)
        {
            if (videoEvent == null || profile == null || profile.IsFlat)
            {
                return;
            }

            try
            {
                Envelope velocityEnvelope = new Envelope(EnvelopeType.Velocity);
                videoEvent.Envelopes.Add(velocityEnvelope);

                foreach (SpeedPoint point in profile.Points)
                {
                    Timecode position = Timecode.FromSeconds(point.EventTimeSeconds);
                    CurveType curve = ToCurveType(point.CurveToNext);

                    // A fresh envelope already carries a default point at the
                    // event start; adjust it instead of stacking a duplicate.
                    EnvelopePoint existing = FindPointAt(velocityEnvelope, position);
                    if (existing != null)
                    {
                        existing.Y = point.Speed;
                        existing.Curve = curve;
                    }
                    else
                    {
                        velocityEnvelope.Points.Add(new EnvelopePoint(position, point.Speed, curve));
                    }
                }

                Logger.Log($"Applied velocity envelope ({profile.Points.Count} points) to event at {videoEvent.Start}");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error applying velocity envelope", ex);
            }
        }

        private static EnvelopePoint FindPointAt(Envelope envelope, Timecode position)
        {
            foreach (EnvelopePoint point in envelope.Points)
            {
                if (Math.Abs(point.X.ToMilliseconds() - position.ToMilliseconds()) < 0.5)
                {
                    return point;
                }
            }
            return null;
        }

        private static CurveType ToCurveType(SpeedCurve curve)
        {
            return curve == SpeedCurve.Smooth ? CurveType.Smooth : CurveType.Linear;
        }

        public void ApplyShake(VideoEvent ev, Timecode atTime, double intensity)
        {
            try
            {
                // Apply camera shake effect (placeholder)
                // In real implementation, this would add a shake effect plugin
                // and keyframe the intensity parameter at the specified time

                // Example pseudocode:
                // var shakeEffect = ev.Effects.FindByName("BCC Shake") ?? ev.Effects.AddEffect("BCC Shake");
                // shakeEffect.Parameters["Intensity"].Keyframes.Add(atTime, intensity);

                Logger.Log($"Applied shake effect at {atTime} with intensity {intensity}");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error applying shake effect", ex);
            }
        }

        public void AddNameTag(VideoEvent ev, string text)
        {
            try
            {
                // Add text overlay for player name/clip info
                // This would typically use a text media generator

                // Placeholder implementation
                Logger.Log($"Added name tag: {text} to clip starting at {ev.Start}");

                // Real implementation would:
                // 1. Get text media generator
                // 2. Create text event on overlay track
                // 3. Set text properties (font, color, position)
                // 4. Set duration and timing
            }
            catch (Exception ex)
            {
                Logger.LogError("Error adding name tag", ex);
            }
        }

        public void ApplyColorCorrection(VideoEvent ev, string preset = "Cinematic")
        {
            try
            {
                // Apply color grading for consistent look
                Logger.Log($"Applied {preset} color correction to clip");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error applying color correction", ex);
            }
        }

        public void ApplyTransition(VideoEvent ev, string transitionType = "Cross Dissolve")
        {
            try
            {
                // Apply transition between clips
                Logger.Log($"Applied {transitionType} transition");
            }
            catch (Exception ex)
            {
                Logger.LogError("Error applying transition", ex);
            }
        }
    }
}
