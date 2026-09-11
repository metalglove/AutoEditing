using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AutoEditing.Iteration.Contracts.Automation;

/// <summary>
/// Creates the stable identifier written into a VEGAS event name. The
/// identifier is independent of timeline position, trim, duration, and speed,
/// so direct editor adjustments do not destroy placement identity.
/// </summary>
public static class CandidatePlacementIdentity
{
	private const string Role = "PLACEMENT";

	public static string Create(
		CandidateWorkspaceId workspace,
		int oneBasedPlacementIndex,
		string mediaPath)
	{
		if (workspace == null) throw new ArgumentNullException(nameof(workspace));
		workspace.Validate();
		if (oneBasedPlacementIndex < 1)
			throw new ArgumentOutOfRangeException(nameof(oneBasedPlacementIndex));
		if (string.IsNullOrWhiteSpace(mediaPath))
			throw new ArgumentException("A placement media path is required.", nameof(mediaPath));
		string normalizedPath = Path.GetFullPath(mediaPath)
			.ToUpperInvariant();
		string digest;
		using (SHA256 sha = SHA256.Create())
			digest = BitConverter.ToString(
					sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath)))
				.Replace("-", "")
				.Substring(0, 16)
				.ToLowerInvariant();
		return workspace.OwnershipPrefix + "|" + Role + "|" +
			oneBasedPlacementIndex.ToString("D4") + "|" + digest;
	}

	public static bool IsOwned(
		CandidateWorkspaceId workspace,
		string placementId)
	{
		if (workspace == null || string.IsNullOrWhiteSpace(placementId))
			return false;
		workspace.Validate();
		return placementId.StartsWith(
			workspace.OwnershipPrefix + "|" + Role + "|",
			StringComparison.Ordinal);
	}
}
