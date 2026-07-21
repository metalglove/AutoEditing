using System.Collections.Generic;
using Core.Domain.Audio;

namespace Core.Domain.Editing;

public sealed class PreparedMontage
{
	public List<ClipPlacement> Placements { get; set; }
	public BeatGrid Beats { get; set; }
}
