using System;
using System.IO;
using System.Linq;
using Core.Domain;
using Core.Domain.Audio;
using Core.Domain.Editing;
using Core.Domain.Planning;

namespace Core.Scripts;

internal static class PreparedMontageResourcePreflight
{
	public static void ValidateAndNormalize(PreparedMontage prepared, string songPath)
	{
		PreparedMontageStructuralValidator.ValidateAndNormalize(prepared);
		if (string.IsNullOrWhiteSpace(songPath) || !File.Exists(songPath))
			throw new FileNotFoundException("The montage song is unavailable.", songPath);
		AudioLoader.GetDurationSeconds(songPath);

		foreach (ClipPlacement placement in prepared.Placements)
		{
			if (!File.Exists(placement.Clip.FilePath))
				throw new FileNotFoundException("A prepared montage clip is unavailable.", placement.Clip.FilePath);
		}

		ShotDetectionConfig config = ConfigurationManager.GetShotDetection();
		SfxTemplateCatalog catalog = SfxTemplateCatalog.Load(config.SfxRoot);
		foreach (string gun in prepared.Placements.Select(item => item.Clip.Gun)
			.Where(item => !string.IsNullOrWhiteSpace(item))
			.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			catalog.ValidateForGun(config.SfxRoot, gun);
		}
	}
}
