using System;
using System.Collections.Generic;

namespace Core.Scripts;

internal static class VegasCommandHandlerRegistry
{
	private static readonly Dictionary<string, IVegasCommandHandler> Handlers = CreateHandlers();

	public static IVegasCommandHandler Get(string commandType)
	{
		IVegasCommandHandler handler;
		if (commandType == null || !Handlers.TryGetValue(commandType, out handler))
			throw new InvalidOperationException("Unknown VEGAS command: " + (commandType ?? "<null>"));
		return handler;
	}

	private static Dictionary<string, IVegasCommandHandler> CreateHandlers()
	{
		IVegasCommandHandler[] handlers = new IVegasCommandHandler[]
		{
			new LayoutAnalysisCommandHandler(), new LayoutSingleClipCommandHandler(),
			new RemoveReviewClipCommandHandler(), new CommitClipReviewCommandHandler(),
			new GetReviewClipSnapshotQueryHandler(), new SetCursorCommandHandler(),
			new LayoutSongAnalysisCommandHandler(), new GetSongReviewSnapshotQueryHandler(), new RemoveSongEventCommandHandler(),
			new UpdateSongEventProjectionCommandHandler(),
			new BuildMontageCommandHandler(),
			new PreflightCandidateCommandHandler(),
			new MaterializeCandidateCommandHandler(),
			new GetCandidateSnapshotCommandHandler(),
			new RenderCandidatePreviewCommandHandler(),
			new CaptureCandidatePreviewFramesCommandHandler(),
			new ApplyCandidateEffectsCommandHandler(),
			new ApplyCandidateAudioCommandHandler(),
			new CleanupCandidateCommandHandler(),
			new PromoteCandidateCommandHandler(),
			new RollbackCandidatePromotionCommandHandler()
		};
		Dictionary<string, IVegasCommandHandler> result = new Dictionary<string, IVegasCommandHandler>(StringComparer.Ordinal);
		foreach (IVegasCommandHandler handler in handlers) result.Add(handler.CommandType, handler);
		return result;
	}
}
