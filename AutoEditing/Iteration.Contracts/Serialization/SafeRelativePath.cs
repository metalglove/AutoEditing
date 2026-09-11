using System;
using System.IO;

namespace AutoEditing.Iteration.Contracts.Serialization;

public static class SafeRelativePath
{
	public static string Validate(string value, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException("A relative path is required.", parameterName);
		if (Path.IsPathRooted(value) || value.IndexOf(':') >= 0)
			throw new ArgumentException("Rooted paths are not allowed.", parameterName);

		string normalized = value.Replace('\\', '/');
		foreach (string segment in normalized.Split('/'))
		{
			if (segment.Length == 0 || segment == "." || segment == "..")
				throw new ArgumentException("Empty and traversal path segments are not allowed.", parameterName);
		}
		return normalized;
	}
}
