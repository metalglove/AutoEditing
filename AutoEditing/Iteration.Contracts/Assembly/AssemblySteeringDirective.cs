using System;

namespace AutoEditing.Iteration.Contracts.Assembly;

/// <summary>
/// Durable editorial direction created at an accepted checkpoint. It applies
/// only to the unaccepted suffix and never rewrites the accepted prefix.
/// </summary>
public sealed class AssemblySteeringDirective
{
	public const int CurrentSchemaVersion = 1;

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;
	public string DirectiveId { get; set; } = "";
	public string SessionId { get; set; } = "";
	public int SourceCheckpoint { get; set; }
	public int ApplicableFromCheckpoint { get; set; }
	public AssemblySteeringScope Scope { get; set; }
	public string SectionId { get; set; } = "";
	public string Instruction { get; set; } = "";
	public DateTimeOffset CreatedUtc { get; set; }
}
