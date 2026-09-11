using System;
using System.IO;
using System.Security.Cryptography;

namespace Core.Domain.Audio.SongAnalysis;

public sealed class SongIdentity
{
	private const int SignatureBlockSize = 1024 * 1024;

	public string ContentFingerprint { get; set; }

	public string LastKnownPath { get; set; }

	public long Size { get; set; }

	public long LastWriteUtcTicks { get; set; }

	public double DurationSeconds { get; set; }

	public static SongIdentity FromFile(string path, double durationSeconds)
	{
		FileInfo file = new FileInfo(path);
		if (!file.Exists)
		{
			throw new FileNotFoundException("Song file not found.", path);
		}
		return new SongIdentity
		{
			ContentFingerprint = ComputeFingerprint(file.FullName),
			LastKnownPath = file.FullName,
			Size = file.Length,
			LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
			DurationSeconds = durationSeconds
		};
	}

	private static string ComputeFingerprint(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using SHA256 hash = SHA256.Create();
		HashBlock(hash, stream, 0, (int)Math.Min(SignatureBlockSize, stream.Length));
		int lastLength = (int)Math.Min(SignatureBlockSize, Math.Max(0, stream.Length - Math.Min(SignatureBlockSize, stream.Length)));
		if (lastLength > 0)
		{
			HashBlock(hash, stream, stream.Length - lastLength, lastLength);
		}
		byte[] length = BitConverter.GetBytes(stream.Length);
		hash.TransformFinalBlock(length, 0, length.Length);
		return BitConverter.ToString(hash.Hash).Replace("-", string.Empty).ToLowerInvariant();
	}

	private static void HashBlock(HashAlgorithm hash, Stream stream, long position, int length)
	{
		stream.Position = position;
		byte[] buffer = new byte[length];
		int offset = 0;
		while (offset < length)
		{
			int read = stream.Read(buffer, offset, length - offset);
			if (read == 0)
			{
				break;
			}
			offset += read;
		}
		hash.TransformBlock(buffer, 0, offset, buffer, 0);
	}
}
