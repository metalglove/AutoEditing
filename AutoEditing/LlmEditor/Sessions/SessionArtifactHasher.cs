using System.Security.Cryptography;

namespace AutoEditing.LlmEditor.Sessions;

internal sealed class SessionArtifactHasher
{
	public string ComputeSha256(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
	}

	public ArtifactIntegrityResult Verify(string path, string expectedSha256)
	{
		if (!File.Exists(path))
		{
			return new ArtifactIntegrityResult(false, "missing", expectedSha256, null);
		}

		string actual = ComputeSha256(path);
		return new ArtifactIntegrityResult(
			string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase),
			string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase) ? null : "hash-mismatch",
			expectedSha256,
			actual);
	}
}

internal sealed record ArtifactIntegrityResult(
	bool IsValid,
	string? ErrorCode,
	string ExpectedSha256,
	string? ActualSha256);
