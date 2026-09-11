using System;

namespace AutoEditing.Iteration.Contracts.Sessions;

public static class EditSessionIdValidator
{
	public static void Validate(string value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			value.Length > 64 ||
			!IsAsciiLetterOrDigit(value[0]))
			throw new ArgumentException(
				"Session ID must start with an ASCII letter or digit and contain at most 64 characters.",
				nameof(value));
		for (int index = 1; index < value.Length; index++)
		{
			char character = value[index];
			if (!IsAsciiLetterOrDigit(character) &&
				character != '-' &&
				character != '_')
				throw new ArgumentException(
					"Session ID may contain only ASCII letters, digits, '-' and '_'.",
					nameof(value));
		}
	}

	private static bool IsAsciiLetterOrDigit(char value) =>
		(value >= 'A' && value <= 'Z') ||
		(value >= 'a' && value <= 'z') ||
		(value >= '0' && value <= '9');
}
