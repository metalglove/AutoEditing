using System;
using System.Text.RegularExpressions;
using AutoEditing.Iteration.Contracts.Sessions;

namespace AutoEditing.Iteration.Contracts.Automation;

public sealed class CandidateWorkspaceId
{
	private static readonly Regex ValidPart = new Regex(
		"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$",
		RegexOptions.CultureInvariant);

	public string SessionId { get; set; } = "";
	public int Iteration { get; set; }
	public string Nonce { get; set; } = "";

	public string OwnershipPrefix => $"AE|LLM|{SessionId}|{Iteration:D4}|{Nonce}";

	public void Validate()
	{
		EditSessionIdValidator.Validate(SessionId);
		if (Iteration < 1)
			throw new ArgumentOutOfRangeException(nameof(Iteration));
		if (!ValidPart.IsMatch(Nonce))
			throw new ArgumentException("Nonce contains unsupported characters.", nameof(Nonce));
	}

	public override string ToString() => OwnershipPrefix;
}
