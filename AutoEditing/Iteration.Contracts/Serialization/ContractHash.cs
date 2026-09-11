using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoEditing.Iteration.Contracts.Serialization;

public static class ContractHash
{
	public static string Compute(JToken value)
	{
		if (value == null)
			throw new ArgumentNullException(nameof(value));
		StringBuilder canonical = new StringBuilder();
		AppendCanonical(canonical, value);
		using (SHA256 sha = SHA256.Create())
		{
			byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
			return BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
		}
	}

	private static void AppendCanonical(StringBuilder output, JToken value)
	{
		if (value is JObject obj)
		{
			output.Append('{');
			foreach (JProperty property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
			{
				AppendText(output, property.Name);
				AppendCanonical(output, property.Value);
			}
			output.Append('}');
			return;
		}
		if (value is JArray array)
		{
			output.Append('[');
			foreach (JToken item in array)
				AppendCanonical(output, item);
			output.Append(']');
			return;
		}

		JValue scalar = value as JValue;
		if (scalar == null)
			throw new InvalidOperationException("Unsupported JSON token type: " + value.Type + ".");
		output.Append((int)value.Type).Append(':');
		switch (value.Type)
		{
			case JTokenType.Null:
			case JTokenType.Undefined:
				return;
			case JTokenType.Integer:
				output.Append(Convert.ToString(scalar.Value, CultureInfo.InvariantCulture));
				return;
			case JTokenType.Float:
				output.Append(Convert.ToDouble(scalar.Value, CultureInfo.InvariantCulture)
					.ToString("G17", CultureInfo.InvariantCulture));
				return;
			case JTokenType.Boolean:
				output.Append(Convert.ToBoolean(scalar.Value, CultureInfo.InvariantCulture) ? '1' : '0');
				return;
			case JTokenType.Date:
				DateTimeOffset date = scalar.Value is DateTimeOffset offset
					? offset
					: new DateTimeOffset(Convert.ToDateTime(
						scalar.Value, CultureInfo.InvariantCulture));
				AppendText(output, date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
				return;
			case JTokenType.Bytes:
				AppendText(output, Convert.ToBase64String((byte[])scalar.Value));
				return;
			default:
				AppendText(output, Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? "");
				return;
		}
	}

	private static void AppendText(StringBuilder output, string text)
	{
		output.Append(text.Length).Append(':').Append(text);
	}
}
