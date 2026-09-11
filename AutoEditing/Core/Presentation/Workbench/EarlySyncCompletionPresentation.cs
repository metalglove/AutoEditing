using System;
using AutoEditing.Iteration.Contracts.Assembly;

namespace Core.Scripts;

internal sealed class EarlySyncCompletionPresentation
{
	public bool CanFinishEarly { get; private set; }
	public string AcceptLabel { get; private set; } = "";
	public string FinishEarlyLabel { get; private set; } = "";
	public string Consequence { get; private set; } = "";

	public static EarlySyncCompletionPresentation Create(
		AssemblySessionState state,
		bool isAwaitingReview)
	{
		if (state == null)
			return new EarlySyncCompletionPresentation
			{
				AcceptLabel = "Accept VEGAS timing & plan next clip",
				FinishEarlyLabel =
					"Accept current clip & finish synchronization early",
				Consequence = "No active synchronization checkpoint."
			};
		int unused = Math.Max(0, state.TotalClips - state.Checkpoint);
		return new EarlySyncCompletionPresentation
		{
			CanFinishEarly = isAwaitingReview && unused > 0,
			AcceptLabel = unused == 0
				? "Accept VEGAS timing & finish sync pass"
				: "Accept VEGAS timing & plan next clip",
			FinishEarlyLabel =
				"Accept current clip & finish early (" + unused + " unused)",
			Consequence = unused == 1
				? "1 selected clip will remain unused."
				: unused + " selected clips will remain unused."
		};
	}
}
