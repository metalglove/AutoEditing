using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Core.Domain.Audio;

public sealed class SfxTemplate
{
	public string Id { get; set; }

	public string Gun { get; set; }

	public List<string> Aliases { get; set; } = new List<string>();

	public string RelativePath { get; set; }

	[JsonConverter(typeof(StringEnumConverter))]
	public ShotOutcome Type { get; set; }

	public double? MuzzleOffsetSeconds { get; set; }

	public double? ConfirmationOffsetSeconds { get; set; }

	public string Fingerprint { get; set; }

	[JsonIgnore]
	public bool IsCalibrated => MuzzleOffsetSeconds.HasValue && ConfirmationOffsetSeconds.HasValue && Finite(MuzzleOffsetSeconds.Value) && Finite(ConfirmationOffsetSeconds.Value) && MuzzleOffsetSeconds.Value >= 0.0 && ConfirmationOffsetSeconds.Value >= 0.0;

	public string FullPath(string root)
	{
		return Path.GetFullPath(Path.Combine(root, RelativePath));
	}

	private static bool Finite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}
}
