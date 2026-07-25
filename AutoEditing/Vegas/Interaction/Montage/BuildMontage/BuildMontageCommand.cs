using Core.Domain.Editing;
namespace Core.Scripts;
internal sealed class BuildMontageCommand : IVegasCommand
{
	public string CommandType => "BuildMontage";
	public PreparedMontage Montage { get; set; }
	public string SongPath { get; set; }
}
