using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class GetCandidateSnapshotCommand : IVegasQuery<CandidateTimelineSnapshot>
{
	public string CommandType => VegasOperations.GetCandidateSnapshot;
	public GetCandidateSnapshotRequest Request { get; set; }
}
