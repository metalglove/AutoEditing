using System.Security.Cryptography;
using System.Text;

namespace Core.Domain.Audio.SongAnalysis;

internal static class MusicAnalysisId
{
	public static string Create(string songFingerprint, string kind, int ordinal)
	{
		string input = (songFingerprint ?? string.Empty) + "|" + kind + "|" + ordinal;
		using SHA256 hash = SHA256.Create();
		byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(input));
		StringBuilder result = new StringBuilder(32);
		for (int index = 0; index < 16; index++)
		{
			result.Append(bytes[index].ToString("x2"));
		}
		return result.ToString();
	}
}
