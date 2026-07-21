using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Core.Domain.Audio;

public sealed class ShotAnalysisSidecar
{
	public const string FileName = ".ae-shot-analysis.json";

	public List<ClipShotAnalysis> Clips { get; set; } = new List<ClipShotAnalysis>();

	public static ShotAnalysisSidecar Load(string clipsFolder)
	{
		string path = Path.Combine(clipsFolder, ".ae-shot-analysis.json");
		if (!File.Exists(path))
		{
			return new ShotAnalysisSidecar();
		}
		return JsonConvert.DeserializeObject<ShotAnalysisSidecar>(File.ReadAllText(path)) ?? new ShotAnalysisSidecar();
	}

	public void Save(string clipsFolder)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		File.WriteAllText(Path.Combine(clipsFolder, ".ae-shot-analysis.json"), JsonConvert.SerializeObject((object)this, (Formatting)1, (JsonConverter[])(object)new JsonConverter[1] { (JsonConverter)new StringEnumConverter() }));
	}

	public ClipShotAnalysis FindValid(string clipPath, string templateFingerprint)
	{
		FileInfo file = new FileInfo(clipPath);
		return Clips.Find((ClipShotAnalysis c) => string.Equals(Path.GetFullPath(c.ClipPath), file.FullName, StringComparison.OrdinalIgnoreCase) && c.Size == file.Length && c.LastWriteUtcTicks == file.LastWriteTimeUtc.Ticks && c.TemplateFingerprint == templateFingerprint);
	}

	public void Put(string clipPath, string templateFingerprint, IEnumerable<ShotEvent> events)
	{
		FileInfo file = new FileInfo(clipPath);
		Clips.RemoveAll((ClipShotAnalysis c) => string.Equals(Path.GetFullPath(c.ClipPath), file.FullName, StringComparison.OrdinalIgnoreCase));
		Clips.Add(new ClipShotAnalysis
		{
			ClipPath = file.FullName,
			Size = file.Length,
			LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
			TemplateFingerprint = templateFingerprint,
			Events = new List<ShotEvent>(events)
		});
	}
}
