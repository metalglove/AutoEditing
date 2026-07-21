using System;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal sealed class VegasHostEventSource : IVegasHostEventSource
{
	private readonly Vegas _vegas;
	private bool _disposed;

	public event EventHandler<VegasHostEventArgs> Changed;

	public VegasHostEventSource(Vegas vegas)
	{
		_vegas = vegas ?? throw new ArgumentNullException("vegas");
		_vegas.MarkersChanged += HandleMarkersChanged;
		_vegas.TimeCursorPositionChanged += HandleCursorChanged;
		_vegas.ProjectOpened += HandleProjectOpened;
		_vegas.ProjectClosed += HandleProjectClosed;
		_vegas.TrackCountChanged += HandleTimelineChanged;
		_vegas.TrackEventCountChanged += HandleTimelineChanged;
		_vegas.TrackEventTimeChanged += HandleTimelineChanged;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_vegas.MarkersChanged -= HandleMarkersChanged;
		_vegas.TimeCursorPositionChanged -= HandleCursorChanged;
		_vegas.ProjectOpened -= HandleProjectOpened;
		_vegas.ProjectClosed -= HandleProjectClosed;
		_vegas.TrackCountChanged -= HandleTimelineChanged;
		_vegas.TrackEventCountChanged -= HandleTimelineChanged;
		_vegas.TrackEventTimeChanged -= HandleTimelineChanged;
	}

	private void HandleMarkersChanged(object sender, EventArgs args) { Publish(VegasHostEventKind.MarkersChanged); }
	private void HandleCursorChanged(object sender, EventArgs args)
	{
		Changed?.Invoke(this, new VegasHostEventArgs
		{
			Kind = VegasHostEventKind.CursorChanged,
			CursorSeconds = _vegas.Transport.CursorPosition.ToMilliseconds() / 1000.0
		});
	}
	private void HandleProjectOpened(object sender, EventArgs args) { Publish(VegasHostEventKind.ProjectOpened); }
	private void HandleProjectClosed(object sender, EventArgs args) { Publish(VegasHostEventKind.ProjectClosed); }
	private void HandleTimelineChanged(object sender, EventArgs args) { Publish(VegasHostEventKind.TimelineChanged); }

	private void Publish(VegasHostEventKind kind)
	{
		Changed?.Invoke(this, new VegasHostEventArgs { Kind = kind });
	}
}
