namespace Core.Domain.Audio.SongAnalysis;

public sealed class EditorialAssignment
{
	public EditorialUse Use { get; set; }

	public EditorialAssignmentOrigin Origin { get; set; } = EditorialAssignmentOrigin.UserChosen;
}
