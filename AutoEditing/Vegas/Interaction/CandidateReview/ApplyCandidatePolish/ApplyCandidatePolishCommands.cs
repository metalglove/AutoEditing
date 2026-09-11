using AutoEditing.Iteration.Contracts.Automation;

namespace Core.Scripts;

internal sealed class ApplyCandidateEffectsCommand :
	IVegasQuery<ApplyCandidateEffectsResult>
{
	public string CommandType => VegasOperations.ApplyCandidateEffects;
	public ApplyCandidateEffectsRequest Request { get; set; }
}

internal sealed class ApplyCandidateAudioCommand :
	IVegasQuery<ApplyCandidateAudioResult>
{
	public string CommandType => VegasOperations.ApplyCandidateAudio;
	public ApplyCandidateAudioRequest Request { get; set; }
}
