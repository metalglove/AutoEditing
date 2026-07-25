using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Core.Domain.Audio;

public static class GunNameNormalizer
{
	private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		{
			"FJX IMPERIUM",
			new string[4] { "FJX IMPERIUM", "FJX", "INTERVENTION", "INTER" }
		},
		{
			"XRK STALKER",
			new string[2] { "XRK STALKER", "XRK" }
		},
		{
			"KATT-AMR",
			new string[3] { "KATT-AMR", "KATT AMR", "KATT" }
		},
		{
			"SP-X 80",
			new string[3] { "SP-X 80", "SPX 80", "SPX" }
		},
		{
			"SP-R 208",
			new string[3] { "SP-R 208", "SPR 208", "SPR" }
		},
		{
			"MORS",
			new string[1] { "MORS" }
		},
		{
			"LONGBOW",
			new string[1] { "LONGBOW" }
		},
		{
			"KAR98K",
			new string[2] { "KAR98K", "KAR 98K" }
		},
		{
			"LOCKWOOD MK2",
			new string[3] { "LOCKWOOD MK2", "MK2 LOCKWOOD", "MK2" }
		},
		{
			"KV INHIBITOR",
			new string[2] { "KV INHIBITOR", "INHIBITOR" }
		},
		{
			"SIGNAL 50",
			new string[2] { "SIGNAL 50", "SIGNAL" }
		}
	};

	public static string Resolve(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string normalized = Clean(raw);
		foreach (KeyValuePair<string, string[]> item in Map)
		{
			if (item.Value.OrderByDescending((string a) => a.Length).Any((string a) => (" " + normalized + " ").Contains(" " + Clean(a) + " ")))
			{
				return item.Key;
			}
		}
		return null;
	}

	public static IEnumerable<string> AliasesFor(string canonical)
	{
		string[] value;
		return Map.TryGetValue(canonical, out value) ? value : new string[1] { canonical };
	}

	private static string Clean(string value)
	{
		return Regex.Replace(value.ToUpperInvariant(), "[^A-Z0-9]+", " ").Trim();
	}
}
