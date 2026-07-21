using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Core.Domain.Audio;

namespace Core.Domain.Clip;

public sealed class ClipSyncLibrary
{
	private const int SignatureBlockSize = 1024 * 1024;

	public List<ClipSyncEntry> Entries { get; set; } = new List<ClipSyncEntry>();

	public static ClipSyncLibrary Load()
	{
		string path = ConfigurationManager.GetSyncLibraryPath();
		if (!File.Exists(path))
		{
			return new ClipSyncLibrary();
		}
		return JsonConvert.DeserializeObject<ClipSyncLibrary>(File.ReadAllText(path)) ?? new ClipSyncLibrary();
	}

	public void Save()
	{
		string json = JsonConvert.SerializeObject(this, Formatting.Indented, new StringEnumConverter());
		File.WriteAllText(ConfigurationManager.GetSyncLibraryPath(), json);
	}

	public ClipSyncEntry Find(string clipPath, string templateFingerprint = null)
	{
		FileInfo file = new FileInfo(clipPath);
		ClipSyncEntry fast = Entries.FirstOrDefault((ClipSyncEntry entry) =>
			string.Equals(Path.GetFullPath(entry.LastKnownPath ?? string.Empty), file.FullName, StringComparison.OrdinalIgnoreCase) &&
			entry.Size == file.Length && entry.LastWriteUtcTicks == file.LastWriteTimeUtc.Ticks);
		if (fast != null)
		{
			return FingerprintMatches(fast, templateFingerprint) ? fast : null;
		}
		string signature = ComputeContentSignature(file.FullName);
		ClipSyncEntry match = Entries.FirstOrDefault((ClipSyncEntry entry) =>
			string.Equals(entry.ContentSignature, signature, StringComparison.OrdinalIgnoreCase));
		if (match != null)
		{
			match.LastKnownPath = file.FullName;
			match.Size = file.Length;
			match.LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks;
		}
		return match != null && FingerprintMatches(match, templateFingerprint) ? match : null;
	}

	public ClipSyncEntry Put(Core.Domain.Clip.Clip clip, string templateFingerprint, IEnumerable<ShotEvent> events, ClipSyncState state)
	{
		FileInfo file = new FileInfo(clip.FilePath);
		string signature = ComputeContentSignature(file.FullName);
		ClipSyncEntry entry = Entries.FirstOrDefault((ClipSyncEntry item) =>
			string.Equals(item.ContentSignature, signature, StringComparison.OrdinalIgnoreCase));
		if (entry == null)
		{
			entry = new ClipSyncEntry { ContentSignature = signature };
			Entries.Add(entry);
		}
		entry.LastKnownPath = file.FullName;
		entry.Size = file.Length;
		entry.LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks;
		entry.TemplateFingerprint = templateFingerprint;
		entry.PlayerName = clip.PlayerName;
		entry.Game = clip.Game;
		entry.Map = clip.Map;
		entry.PrimaryGun = clip.Gun;
		entry.DurationSeconds = clip.DurationSeconds;
		entry.Events = new List<ShotEvent>(events ?? Enumerable.Empty<ShotEvent>());
		entry.State = state;
		entry.ReviewedUtc = state == ClipSyncState.Ready ? (DateTime?)DateTime.UtcNow : entry.ReviewedUtc;
		return entry;
	}

	public static string ComputeContentSignature(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using SHA256 hash = SHA256.Create();
		int firstLength = (int)Math.Min(SignatureBlockSize, stream.Length);
		byte[] first = ReadBlock(stream, firstLength);
		hash.TransformBlock(first, 0, first.Length, first, 0);
		int lastLength = (int)Math.Min(SignatureBlockSize, Math.Max(0, stream.Length - firstLength));
		if (lastLength > 0)
		{
			stream.Position = stream.Length - lastLength;
			byte[] last = ReadBlock(stream, lastLength);
			hash.TransformBlock(last, 0, last.Length, last, 0);
		}
		byte[] length = BitConverter.GetBytes(stream.Length);
		hash.TransformFinalBlock(length, 0, length.Length);
		return BitConverter.ToString(hash.Hash).Replace("-", string.Empty).ToLowerInvariant();
	}

	private static byte[] ReadBlock(Stream stream, int length)
	{
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
		return buffer;
	}

	private static bool FingerprintMatches(ClipSyncEntry entry, string templateFingerprint)
	{
		return string.IsNullOrEmpty(templateFingerprint) ||
			string.Equals(entry.TemplateFingerprint, templateFingerprint, StringComparison.Ordinal);
	}
}
