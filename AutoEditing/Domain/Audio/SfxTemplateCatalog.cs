using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Core.Domain.Audio;

public sealed class SfxTemplateCatalog
{
	public const string FileName = "shot-templates.json";

	public List<string> UnsupportedGuns { get; set; } = new List<string> { "SIGNAL 50" };

	public List<SfxTemplate> Templates { get; set; } = new List<SfxTemplate>();

	public static SfxTemplateCatalog Load(string root)
	{
		string text = Path.Combine(root, "shot-templates.json");
		if (!File.Exists(text))
		{
			throw new InvalidOperationException("SFX catalog not found. Run Calibrate SFX first: " + text);
		}
		SfxTemplateCatalog sfxTemplateCatalog = JsonConvert.DeserializeObject<SfxTemplateCatalog>(File.ReadAllText(text));
		if (sfxTemplateCatalog == null)
		{
			throw new InvalidOperationException("Invalid SFX catalog: " + text);
		}
		sfxTemplateCatalog.Validate(root);
		return sfxTemplateCatalog;
	}

	public static SfxTemplateCatalog Discover(string root)
	{
		if (!Directory.Exists(root))
		{
			throw new DirectoryNotFoundException(root);
		}
		SfxTemplateCatalog sfxTemplateCatalog = new SfxTemplateCatalog();
		foreach (string item in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories).Where(IsAudio).OrderBy((string p) => p, StringComparer.OrdinalIgnoreCase))
		{
			string text = Relative(root, item);
			string text2 = GunNameNormalizer.Resolve(text);
			if (text2 != null)
			{
				ShotOutcome type = InferType(text);
				sfxTemplateCatalog.Templates.Add(new SfxTemplate
				{
					Id = text2 + ":" + type.ToString() + ":" + sfxTemplateCatalog.Templates.Count,
					Gun = text2,
					Aliases = GunNameNormalizer.AliasesFor(text2).ToList(),
					RelativePath = Relative(root, item),
					Type = type,
					Fingerprint = Fingerprint(item)
				});
			}
		}
		return sfxTemplateCatalog;
	}

	public void Save(string root)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		File.WriteAllText(Path.Combine(root, "shot-templates.json"), JsonConvert.SerializeObject((object)this, (Formatting)1, (JsonConverter[])(object)new JsonConverter[1] { (JsonConverter)new StringEnumConverter() }));
	}

	public IReadOnlyList<SfxTemplate> ForGun(string rawGun)
	{
		string gun = GunNameNormalizer.Resolve(rawGun);
		if (gun == null || UnsupportedGuns.Any((string g) => GunNameNormalizer.Resolve(g) == gun))
		{
			return new List<SfxTemplate>();
		}
		return Templates.Where((SfxTemplate t) => GunNameNormalizer.Resolve(t.Gun) == gun).ToList();
	}

	public void ValidateForGun(string root, string rawGun)
	{
		IReadOnlyList<SfxTemplate> readOnlyList = ForGun(rawGun);
		if (readOnlyList.Count == 0)
		{
			throw new InvalidOperationException("Unsupported gun or no templates: " + rawGun);
		}
		ValidateTemplates(root, readOnlyList);
	}

	public void Validate(string root)
	{
		ValidateTemplates(root, Templates);
	}

	private static void ValidateTemplates(string root, IEnumerable<SfxTemplate> templates)
	{
		foreach (SfxTemplate template in templates)
		{
			string text = template.FullPath(root);
			if (!File.Exists(text))
			{
				throw new InvalidOperationException("Missing SFX template: " + text);
			}
			if (!string.Equals(template.Fingerprint, Fingerprint(text), StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("Template changed; recalibration required: " + text);
			}
			if (!template.IsCalibrated)
			{
				throw new InvalidOperationException("Template lacks valid SHOT/CONFIRM calibration: " + text);
			}
		}
	}

	public string RelevantFingerprint(string gun)
	{
		string s = "log-spectral-v18-mors-gap-recovery|" + string.Join("|", from t in ForGun(gun)
			orderby t.Id
			select t.Id + ":" + t.Fingerprint + ":" + t.MuzzleOffsetSeconds + ":" + t.ConfirmationOffsetSeconds);
		using SHA256 sHA = SHA256.Create();
		return Hex(sHA.ComputeHash(Encoding.UTF8.GetBytes(s)));
	}

	public static string Fingerprint(string path)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(path);
		return Hex(sHA.ComputeHash(inputStream));
	}

	private static string Hex(byte[] bytes)
	{
		return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
	}

	private static bool IsAudio(string path)
	{
		string extension = Path.GetExtension(path);
		return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase) || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase);
	}

	private static ShotOutcome InferType(string name)
	{
		string text = name.ToLowerInvariant();
		if (text.Contains("head"))
		{
			return ShotOutcome.Headshot;
		}
		if (text.Contains("miss"))
		{
			return ShotOutcome.Miss;
		}
		if (text.Contains("bolt"))
		{
			return ShotOutcome.Bolt;
		}
		if (text.Contains("reload"))
		{
			return ShotOutcome.Reload;
		}
		if (text.Contains("hit"))
		{
			return ShotOutcome.Hit;
		}
		return ShotOutcome.Shot;
	}

	private static string Relative(string root, string path)
	{
		Uri uri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
		return Uri.UnescapeDataString(uri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString()).Replace('/', Path.DirectorySeparatorChar);
	}
}
