using AutoEditing.Iteration.Contracts.Serialization;
using AutoEditing.Iteration.Contracts.Steering;
using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Workbench;

public sealed class FileWorkbenchSteeringSink : IWorkbenchSteeringSink
{
	private readonly Func<Guid> createId;
	private readonly AtomicFileWriter writer = new();

	public FileWorkbenchSteeringSink(Func<Guid>? createId = null) =>
		this.createId = createId ?? Guid.NewGuid;

	public EditSteeringDirective Submit(string sessionRoot, WorkbenchSteeringSubmission submission)
	{
		ArgumentNullException.ThrowIfNull(submission);
		if (string.IsNullOrWhiteSpace(submission.Instruction))
			throw new ArgumentException("A steering instruction is required.", nameof(submission));
		if (submission.TimelineStart.HasValue != submission.TimelineEnd.HasValue)
			throw new ArgumentException("A steering range requires both a start and an end.", nameof(submission));
		if (submission.TimelineStart < TimeSpan.Zero ||
			submission.TimelineEnd < submission.TimelineStart)
			throw new ArgumentException("The steering range is invalid.", nameof(submission));

		FileWorkbenchProjectionReader reader = new();
		WorkbenchSession session = reader.Read(sessionRoot);
		EditSteeringDirective directive = new()
		{
			DirectiveId = createId().ToString("N"),
			Kind = submission.Kind,
			Instruction = submission.Instruction.Trim(),
			TargetIds = (submission.TargetIds ?? Array.Empty<string>())
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.Select(value => value.Trim())
				.Distinct(StringComparer.Ordinal)
				.ToArray(),
			TimelineStart = submission.TimelineStart,
			TimelineEnd = submission.TimelineEnd,
			ApplicableFromIteration = submission.ApplicableFromIteration ??
				Math.Max(1, session.CurrentIteration + 1),
			IsActive = true
		};
		if (directive.ApplicableFromIteration < 1)
			throw new ArgumentOutOfRangeException(nameof(submission), "The applicable iteration must be positive.");

		SessionPathResolver paths = new(sessionRoot);
		string path = paths.Resolve($"steering/{directive.DirectiveId}.json");
		if (File.Exists(path)) throw new IOException("The generated steering directive already exists.");
		writer.WriteText(path, ContractSerializer.Serialize(directive));
		return directive;
	}
}
