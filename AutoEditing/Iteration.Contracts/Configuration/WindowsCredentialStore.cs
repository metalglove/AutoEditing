using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoEditing.Iteration.Contracts.Configuration;

public static class WindowsCredentialStore
{
	private const uint GenericCredential = 1;
	private const uint PersistLocalMachine = 2;
	private const int ElementNotFound = 1168;

	public static bool Exists(string target) =>
		TryRead(target, out _);

	public static string Read(string target)
	{
		if (!TryRead(target, out string secret))
			return null;
		return secret;
	}

	public static void Write(string target, string secret)
	{
		ValidateTarget(target);
		if (string.IsNullOrWhiteSpace(secret))
			throw new ArgumentException(
				"A non-empty credential is required.", nameof(secret));
		byte[] bytes = Encoding.UTF8.GetBytes(secret);
		IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
		try
		{
			Marshal.Copy(bytes, 0, blob, bytes.Length);
			Credential credential = new()
			{
				Type = GenericCredential,
				TargetName = target,
				CredentialBlobSize = (uint)bytes.Length,
				CredentialBlob = blob,
				Persist = PersistLocalMachine,
				UserName = Environment.UserName
			};
			if (!CredWrite(ref credential, 0))
				throw new Win32Exception(
					Marshal.GetLastWin32Error(),
					"Windows Credential Manager rejected the API key.");
		}
		finally
		{
			Array.Clear(bytes, 0, bytes.Length);
			ZeroAndFree(blob, bytes.Length);
		}
	}

	public static bool Delete(string target)
	{
		ValidateTarget(target);
		if (CredDelete(target, GenericCredential, 0))
			return true;
		int error = Marshal.GetLastWin32Error();
		if (error == ElementNotFound)
			return false;
		throw new Win32Exception(
			error,
			"Windows Credential Manager could not delete the API key.");
	}

	private static bool TryRead(string target, out string secret)
	{
		ValidateTarget(target);
		secret = null;
		if (!CredRead(target, GenericCredential, 0, out IntPtr pointer))
		{
			int error = Marshal.GetLastWin32Error();
			if (error == ElementNotFound)
				return false;
			throw new Win32Exception(
				error,
				"Windows Credential Manager could not read the API key.");
		}
		try
		{
			Credential credential =
				Marshal.PtrToStructure<Credential>(pointer);
			if (credential.CredentialBlob == IntPtr.Zero ||
				credential.CredentialBlobSize == 0)
				return false;
			byte[] bytes = new byte[
				checked((int)credential.CredentialBlobSize)];
			try
			{
				Marshal.Copy(
					credential.CredentialBlob,
					bytes,
					0,
					bytes.Length);
				secret = Encoding.UTF8.GetString(bytes);
				return secret.Length > 0;
			}
			finally
			{
				Array.Clear(bytes, 0, bytes.Length);
			}
		}
		finally
		{
			CredFree(pointer);
		}
	}

	private static void ValidateTarget(string target)
	{
		if (string.IsNullOrWhiteSpace(target) ||
			target.Length > 256)
			throw new ArgumentException(
				"A valid credential target is required.", nameof(target));
	}

	private static void ZeroAndFree(IntPtr pointer, int length)
	{
		if (pointer == IntPtr.Zero)
			return;
		for (int index = 0; index < length; index++)
			Marshal.WriteByte(pointer, index, 0);
		Marshal.FreeHGlobal(pointer);
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct Credential
	{
		public uint Flags;
		public uint Type;
		public string TargetName;
		public string Comment;
		public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
		public uint CredentialBlobSize;
		public IntPtr CredentialBlob;
		public uint Persist;
		public uint AttributeCount;
		public IntPtr Attributes;
		public string TargetAlias;
		public string UserName;
	}

	[DllImport("advapi32.dll", EntryPoint = "CredWriteW",
		CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CredWrite(
		[In] ref Credential credential,
		uint flags);

	[DllImport("advapi32.dll", EntryPoint = "CredReadW",
		CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CredRead(
		string target,
		uint type,
		uint flags,
		out IntPtr credential);

	[DllImport("advapi32.dll", EntryPoint = "CredDeleteW",
		CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CredDelete(
		string target,
		uint type,
		uint flags);

	[DllImport("advapi32.dll")]
	private static extern void CredFree(IntPtr credential);
}
