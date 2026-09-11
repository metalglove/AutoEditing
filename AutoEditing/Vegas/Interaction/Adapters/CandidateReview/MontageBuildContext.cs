using System;
using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class MontageBuildContext
{
	private MontageBuildContext(
		bool isCandidate,
		CandidateWorkspaceId workspace,
		bool addMarkers,
		bool applyEffects,
		bool includeSong,
		bool includeSfx,
		bool matchProjectVideoProperties)
	{
		IsCandidate = isCandidate;
		Workspace = workspace;
		AddMarkers = addMarkers;
		ApplyEffects = applyEffects;
		IncludeSong = includeSong;
		IncludeSfx = includeSfx;
		MatchProjectVideoProperties = matchProjectVideoProperties;
	}

	public bool IsCandidate { get; }
	public CandidateWorkspaceId Workspace { get; }
	public bool AddMarkers { get; }
	public bool ApplyEffects { get; }
	public bool IncludeSong { get; }
	public bool IncludeSfx { get; }
	public bool MatchProjectVideoProperties { get; }

	public string VideoTrackName => IsCandidate ? CandidateTrackNaming.Video(Workspace) : "AE|Montage Clips";
	public string SongTrackName => IsCandidate ? CandidateTrackNaming.Song(Workspace) : "AE|Montage Song";
	public string SfxTrackName(int oneBasedIndex) =>
		IsCandidate ? CandidateTrackNaming.Sfx(Workspace, oneBasedIndex) : "AE|Montage Gun SFX " + oneBasedIndex;

	public static MontageBuildContext Production(bool applyEffects)
	{
		return new MontageBuildContext(
			isCandidate: false,
			workspace: null,
			addMarkers: true,
			applyEffects: applyEffects,
			includeSong: true,
			includeSfx: true,
			matchProjectVideoProperties: true);
	}

	public static MontageBuildContext Candidate(
		CandidateWorkspaceId workspace,
		bool applyEffects = true,
		bool includeSong = true,
		bool includeSfx = true)
	{
		if (workspace == null) throw new ArgumentNullException(nameof(workspace));
		workspace.Validate();
		return new MontageBuildContext(
			isCandidate: true,
			workspace: workspace,
			addMarkers: false,
			applyEffects: applyEffects,
			includeSong: includeSong,
			includeSfx: includeSfx,
			matchProjectVideoProperties: false);
	}
}
