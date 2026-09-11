using AutoEditing.LlmEditor.Sessions;

namespace AutoEditing.LlmEditor.Automation;

internal sealed class VegasAutomationSpoolPaths
{
	private readonly SessionPathResolver paths;

	public VegasAutomationSpoolPaths(string spoolRoot)
	{
		paths = new SessionPathResolver(spoolRoot);
		Directory.CreateDirectory(Requests);
		Directory.CreateDirectory(Running);
		Directory.CreateDirectory(Responses);
		Directory.CreateDirectory(Submissions);
		Directory.CreateDirectory(Locks);
	}

	public string Root => paths.Root;
	public string Requests => paths.Resolve("ipc/requests");
	public string Running => paths.Resolve("ipc/running");
	public string Responses => paths.Resolve("ipc/responses");
	public string Submissions => paths.Resolve("submissions");
	public string Locks => paths.Resolve("locks");
	public string SubmissionLock => paths.Resolve("locks/submission.lock");
	public string Request(string jobId) => paths.Resolve("ipc/requests/" + FileName(jobId));
	public string RunningJob(string jobId) => paths.Resolve("ipc/running/" + FileName(jobId));
	public string Response(string jobId) => paths.Resolve("ipc/responses/" + jobId + ".response.json");
	public string Submission(string jobId) => paths.Resolve("submissions/" + FileName(jobId));

	private static string FileName(string jobId)
	{
		if (string.IsNullOrWhiteSpace(jobId) ||
			jobId.Any(character => !(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.')))
			throw new ArgumentException("JobId contains unsupported path characters.", nameof(jobId));
		return jobId + ".json";
	}
}
