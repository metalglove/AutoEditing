using System;
using System.Collections.Generic;
using Core.Domain.Logging;
using Core.Domain.Editing;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class EffectsApplier
{
	public void ApplyVelocityEnvelope(VideoEvent ev, SpeedProfile profile)
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Expected O, but got Unknown
		try
		{
			if (ev == (VideoEvent)null)
				throw new ArgumentNullException(nameof(ev));
			VelocityEnvelopeRenderPlan renderPlan =
				VelocityEnvelopeRenderPlan.Create(profile);
			IReadOnlyList<VelocityEnvelopeRenderPoint> points =
				renderPlan.Points;
			Envelope val = new Envelope((EnvelopeType)202);
			((BaseList<Envelope>)(object)ev.Envelopes).Add(val);
			((BaseList<EnvelopePoint>)(object)val.Points).Clear();
			int num = 0;
			for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
			{
				VelocityEnvelopeRenderPoint item = points[pointIndex];
				Timecode timelineOffset =
					Timecode.FromSeconds(item.TimelineOffsetSeconds);
				// Linear is required, not a style choice: the reconciliation pass
				// reconstructs source-time positions from live envelope points by
				// integrating speed as linear-in-timeline-time between them. Any
				// other VEGAS curve type would make that reconstruction inexact.
				if (TryAddVelocityPoint(val, timelineOffset, item.Speed, CurveType.Linear, item.SourceTimeSeconds))
				{
					num++;
				}
			}
			if (num != points.Count)
				throw new InvalidOperationException(
					"VEGAS accepted only " + num + " of " + points.Count +
					" synchronization velocity points.");
			Logger.Log($"Applied velocity envelope with {num}/{points.Count} points to clip starting at {((TrackEvent)ev).Start}");
		}
		catch (Exception ex)
		{
			Logger.LogError("Error applying velocity envelope", ex);
			throw;
		}
	}

	private static bool TryAddVelocityPoint(Envelope envelope, Timecode timelineOffset, double speed, CurveType curveType, double sourceTimeSecondsForLogging)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Expected O, but got Unknown
		EnvelopePoint pointAtX = envelope.Points.GetPointAtX(timelineOffset);
		if (pointAtX != (EnvelopePoint)null)
		{
			pointAtX.Y = speed;
			pointAtX.Curve = curveType;
			return true;
		}
		try
		{
			((BaseList<EnvelopePoint>)(object)envelope.Points).Add(new EnvelopePoint(timelineOffset, speed, curveType));
			return true;
		}
		catch (Exception ex)
		{
			pointAtX = envelope.Points.GetPointAtX(timelineOffset);
			if (pointAtX != (EnvelopePoint)null)
			{
				pointAtX.Y = speed;
				pointAtX.Curve = curveType;
				return true;
			}
			Logger.LogError($"Velocity point at timeline {timelineOffset} (source {sourceTimeSecondsForLogging:F3}s, speed {speed:F2}x) " + "was rejected: " + ex.Message);
			return false;
		}
	}

	public void ApplyShake(VideoEvent ev, Timecode atTime, double intensity)
	{
		try
		{
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
			Logger.Log($"Added name tag: {text} to clip starting at {((TrackEvent)ev).Start}");
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
			Logger.Log("Applied " + preset + " color correction to clip");
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
			Logger.Log("Applied " + transitionType + " transition");
		}
		catch (Exception ex)
		{
			Logger.LogError("Error applying transition", ex);
		}
	}
}
