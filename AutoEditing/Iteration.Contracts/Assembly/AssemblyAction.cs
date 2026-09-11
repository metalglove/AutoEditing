using System;

namespace AutoEditing.Iteration.Contracts.Assembly;

public enum AssemblySteeringScope
{
	CurrentClip,
	NextClip,
	RemainingSection,
	GlobalRemainder
}

public static class FinalizationActionTargets
{
	public const string PromoteWithoutFinalPreview =
		"promote-without-final-preview";
	public const string RenderFinalPreview =
		"render-final-preview";

	public static bool IsSupported(string targetId) =>
		string.Equals(
			targetId,
			PromoteWithoutFinalPreview,
			StringComparison.Ordinal) ||
		string.Equals(
			targetId,
			RenderFinalPreview,
			StringComparison.Ordinal);
}

public sealed class AssemblyAction
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string ActionId { get; set; } = Guid.NewGuid().ToString("N");
	public string SessionId { get; set; } = "";
	public int Checkpoint { get; set; }
	public long ExpectedStateRevision { get; set; }
	public AssemblyActionKind Kind { get; set; }
	public string TargetId { get; set; } = "";
	public string Instruction { get; set; } = "";
	public AssemblySteeringScope SteeringScope { get; set; } =
		AssemblySteeringScope.CurrentClip;
	public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
