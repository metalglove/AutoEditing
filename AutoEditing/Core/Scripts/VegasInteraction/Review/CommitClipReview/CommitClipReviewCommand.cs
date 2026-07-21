using System.Collections.Generic;
using Core.Domain.Audio;
namespace Core.Scripts;
internal sealed class CommitClipReviewCommand : IVegasCommand
{
	public string CommandType => "CommitClipReview";
	public string ClipsFolder { get; set; }
	public string SfxRoot { get; set; }
	public int ClipIndex { get; set; }
	public List<ReviewMarkerSubmission> ReviewedMarkers { get; set; }
}
