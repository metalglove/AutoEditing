using System;
using Core.Domain.Logging;
using ScriptPortal.Vegas;

namespace Core.Scripts;

/// <summary>
/// Renders the small subset of editorial treatments that can be produced with
/// the built-in VEGAS scripting API and no installed OFX dependencies.
/// Unsupported treatments are reported explicitly and never logged as applied.
/// </summary>
internal sealed class VegasEditorialEffectRenderer
{
	public EditorialEffectRenderResult Render(VideoEvent videoEvent, EditorialEffectRenderAction action)
	{
		if (videoEvent == null) throw new ArgumentNullException(nameof(videoEvent));
		if (action == null) throw new ArgumentNullException(nameof(action));

		try
		{
			switch (action.Kind)
			{
				case EditorialEffectRenderKind.ScreenPump:
					return RenderScreenPump(videoEvent, action);
				case EditorialEffectRenderKind.WhiteFlash:
					return EditorialEffectRenderResult.Unsupported(
						"White flash needs an overlay track and generated-media plug-in resolution.");
				case EditorialEffectRenderKind.Transition:
					return EditorialEffectRenderResult.Unsupported(
						"Transitions need adjacent-event overlap and plug-in resolution.");
				case EditorialEffectRenderKind.Shake:
					return EditorialEffectRenderResult.Unsupported(
						"Shake rendering is not implemented without an OFX dependency.");
				case EditorialEffectRenderKind.TitleReveal:
					return EditorialEffectRenderResult.Unsupported(
						"Title rendering needs generated-media plug-in resolution.");
				case EditorialEffectRenderKind.ColorCorrection:
					return EditorialEffectRenderResult.Unsupported(
						"Color correction needs an available OFX effect or preset.");
				default:
					return EditorialEffectRenderResult.Unsupported("Unknown editorial treatment.");
			}
		}
		catch (Exception ex)
		{
			Logger.LogError("Error rendering " + action.Kind, ex);
			return EditorialEffectRenderResult.Unsupported("VEGAS rejected the effect: " + ex.Message);
		}
	}

	private static EditorialEffectRenderResult RenderScreenPump(
		VideoEvent videoEvent,
		EditorialEffectRenderAction action)
	{
		double eventLength = ((TrackEvent)videoEvent).Length.ToMilliseconds() / 1000.0;
		if (action.EventTimeSeconds > eventLength + 0.0005)
			return EditorialEffectRenderResult.Unsupported("The pump time is outside the video event.");

		VideoMotionKeyframes keyframes = videoEvent.VideoMotion.Keyframes;
		if (keyframes.Count == 0)
			return EditorialEffectRenderResult.Unsupported("The generated event has no baseline pan/crop keyframe.");

		VideoMotionKeyframe original = keyframes[0];
		ScreenPumpShape shape = ScreenPumpShape.Create(
			action.EventTimeSeconds,
			action.DurationSeconds,
			eventLength,
			action.NextPumpStartSeconds);
		if (shape.AfterSeconds - shape.BeforeSeconds < 0.010)
			return EditorialEffectRenderResult.Unsupported("The event has insufficient room for a pump.");

		// A keyframe's interpolation shapes the segment that leaves it: Fast punches in and
		// settles onto the peak, Smooth eases the release back to the baseline, and the
		// closing baseline keyframe only holds until the next pump.
		VideoMotionBounds baseline = CloneBounds(original.Bounds);
		if (shape.HasAttack)
			AddOrUpdateKeyframe(keyframes, shape.BeforeSeconds, baseline, VideoKeyframeType.Fast);

		// Pan/crop zooms in by shrinking the source rectangle around its center.
		double zoom = 1.025 + (0.075 * action.Intensity);
		VideoMotionBounds peak = ScaleBoundsAroundCenter(baseline, 1.0 / zoom);
		AddOrUpdateKeyframe(keyframes, shape.PeakSeconds, peak, VideoKeyframeType.Smooth);
		if (shape.HasRelease)
			AddOrUpdateKeyframe(keyframes, shape.AfterSeconds, baseline, VideoKeyframeType.Linear);

		return EditorialEffectRenderResult.Success(
			$"Screen pump rendered at {shape.PeakSeconds:F3}s ({zoom:F3}x zoom, " +
			$"{shape.PeakSeconds - shape.BeforeSeconds:F3}s in, {shape.AfterSeconds - shape.PeakSeconds:F3}s out).");
	}

	private static void AddOrUpdateKeyframe(
		VideoMotionKeyframes keyframes,
		double seconds,
		VideoMotionBounds bounds,
		VideoKeyframeType type)
	{
		Timecode position = Timecode.FromSeconds(seconds);
		for (int i = 0; i < keyframes.Count; i++)
		{
			if (Math.Abs(keyframes[i].Position.ToMilliseconds() - position.ToMilliseconds()) < 0.5)
			{
				keyframes[i].Bounds = CloneBounds(bounds);
				keyframes[i].Type = type;
				return;
			}
		}

		// VEGAS validates Bounds against the owning VideoMotion object. A new
		// keyframe therefore has to be attached before any geometry is set.
		VideoMotionKeyframe keyframe = new VideoMotionKeyframe(position);
		keyframes.Add(keyframe);
		try
		{
			keyframe.Bounds = CloneBounds(bounds);
			keyframe.Type = type;
			if (!keyframe.IsValid())
				throw new ApplicationException("VEGAS left the attached video motion keyframe invalid.");
		}
		catch
		{
			keyframes.Remove(keyframe);
			throw;
		}
	}

	private static VideoMotionBounds ScaleBoundsAroundCenter(VideoMotionBounds bounds, double factor)
	{
		float centerX = (bounds.TopLeft.X + bounds.BottomRight.X) * 0.5f;
		float centerY = (bounds.TopLeft.Y + bounds.BottomRight.Y) * 0.5f;
		return new VideoMotionBounds(
			ScaleVertex(bounds.TopLeft, centerX, centerY, factor),
			ScaleVertex(bounds.TopRight, centerX, centerY, factor),
			ScaleVertex(bounds.BottomRight, centerX, centerY, factor),
			ScaleVertex(bounds.BottomLeft, centerX, centerY, factor));
	}

	private static VideoMotionVertex ScaleVertex(
		VideoMotionVertex vertex,
		float centerX,
		float centerY,
		double factor)
	{
		return new VideoMotionVertex(
			(float)(centerX + ((vertex.X - centerX) * factor)),
			(float)(centerY + ((vertex.Y - centerY) * factor)));
	}

	private static VideoMotionBounds CloneBounds(VideoMotionBounds bounds)
	{
		return new VideoMotionBounds(
			new VideoMotionVertex(bounds.TopLeft.X, bounds.TopLeft.Y),
			new VideoMotionVertex(bounds.TopRight.X, bounds.TopRight.Y),
			new VideoMotionVertex(bounds.BottomRight.X, bounds.BottomRight.Y),
			new VideoMotionVertex(bounds.BottomLeft.X, bounds.BottomLeft.Y));
	}
}
