using System;
using System.IO;
using System.Text;

namespace AutoEditing.Iteration.Contracts.Assembly;

public sealed class AssemblyRuntimeLease : IDisposable
{
	private readonly FileStream stream;

	private AssemblyRuntimeLease(string path, FileStream stream)
	{
		Path = path;
		this.stream = stream;
	}

	public string Path { get; }

	public static AssemblyRuntimeLease Acquire(string sessionRoot, string sessionId)
	{
		if (string.IsNullOrWhiteSpace(sessionRoot))
			throw new ArgumentException("A session root is required.", nameof(sessionRoot));
		if (string.IsNullOrWhiteSpace(sessionId))
			throw new ArgumentException("A session ID is required.", nameof(sessionId));
		string directory = System.IO.Path.Combine(
			System.IO.Path.GetFullPath(sessionRoot), "assembly");
		Directory.CreateDirectory(directory);
		string path = System.IO.Path.Combine(directory, "runtime.lock");
		FileStream stream;
		try
		{
			stream = new FileStream(
				path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		}
		catch (IOException exception)
		{
			throw new InvalidOperationException(
				"Another companion process already owns assembly session '" +
				sessionId + "'.",
				exception);
		}
		stream.SetLength(0);
		byte[] owner = new UTF8Encoding(false).GetBytes(
			sessionId + "\n" +
			System.Diagnostics.Process.GetCurrentProcess().Id.ToString(
				System.Globalization.CultureInfo.InvariantCulture) + "\n" +
			DateTimeOffset.UtcNow.ToString("O"));
		stream.Write(owner, 0, owner.Length);
		stream.Flush(true);
		return new AssemblyRuntimeLease(path, stream);
	}

	public static bool IsHeld(string sessionRoot)
	{
		string path = System.IO.Path.Combine(
			System.IO.Path.GetFullPath(sessionRoot), "assembly", "runtime.lock");
		if (!File.Exists(path)) return false;
		try
		{
			using FileStream probe = new(
				path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
			return false;
		}
		catch (IOException)
		{
			return true;
		}
	}

	public void Dispose() => stream.Dispose();
}
