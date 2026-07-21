using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Domain.Audio;

public sealed class ShotDetector
{
	private sealed class Match
	{
		public double StartSeconds;

		public double AnchorSeconds;

		public double Score;

		public SfxTemplate Template;

		public double TransientStrength;

		public double GunLikeness;

		public double TemplatePeakDelaySeconds;

		public double TemplateAlignedOnsetSeconds;

		public double TransientPeakSeconds;

		public bool Supplemental;
	}

	private sealed class Transient
	{
		public double TimeSeconds;

		public double Strength;

		public double GunLikeness;

		public bool Supplemental;
	}

	private sealed class SpectralFeatures
	{
		public float[] Values;

		public float[] FrameWeights;

		public float[] EnergyEnvelope;

		public int FrameCount;

		public double HopSeconds;

		public static SpectralFeatures Create(MonoAudio audio)
		{
			int num = Math.Max(0, 1 + (audio.Samples.Length - 256) / 256);
			float[] array = new float[num * 32];
			float[] array2 = new float[num];
			float num2 = 0f;
			for (int i = 0; i < num; i++)
			{
				int num3 = i * 256;
				double num4 = 0.0;
				for (int j = 0; j < 256; j++)
				{
					double num5 = audio.Samples[num3 + j];
					num4 += num5 * num5;
				}
				array2[i] = (float)Math.Sqrt(num4 / 256.0);
				num2 = Math.Max(num2, array2[i]);
				for (int k = 0; k < 32; k++)
				{
					int num6 = 1 + k * 127 / 32;
					double num7 = 0.0;
					double num8 = 0.0;
					for (int l = 0; l < 256; l++)
					{
						double num9 = (double)audio.Samples[num3 + l] * (0.5 - 0.5 * Math.Cos(Math.PI * 2.0 * (double)l / 255.0));
						double num10 = Math.PI * 2.0 * (double)num6 * (double)l / 256.0;
						num7 += num9 * Math.Cos(num10);
						num8 -= num9 * Math.Sin(num10);
					}
					array[i * 32 + k] = (float)Math.Log(1E-08 + num7 * num7 + num8 * num8);
				}
			}
			float[] array3 = new float[num];
			float[] array4 = new float[num];
			float num11 = num2 * 0.03f;
			for (int m = 0; m < num; m++)
			{
				array3[m] = array2[m] / Math.Max(num2, 1E-09f);
				array4[m] = ((array2[m] <= num11) ? 0f : ((float)Math.Sqrt(array3[m])));
			}
			return new SpectralFeatures
			{
				Values = array,
				FrameWeights = array4,
				EnergyEnvelope = array3,
				FrameCount = num,
				HopSeconds = 256.0 / (double)audio.SampleRate
			};
		}
	}

	private const int Window = 256;

	private const int Hop = 256;

	private const int Bands = 32;

	public const string AnalysisVersion = "log-spectral-v18-mors-gap-recovery";

	public const double CandidateThreshold = 0.76;

	public const double HighConfidenceThreshold = 0.82;

	private const double NegativeMatchThreshold = 0.68;

	private const double NegativeVetoSeconds = 0.25;

	private const double MergeSeconds = 0.3;

	private const double TemplatePreRollSeconds = 0.02;

	private const double TemplatePostRollSeconds = 0.45;

	private const double TransientSnapSeconds = 0.18;

	private const double SupplementalNegativeMargin = 0.13;

	public List<ShotEvent> DetectShots(MonoAudio clipAudio, string gun, SfxTemplateCatalog catalog, string sfxRoot)
	{
		IReadOnlyList<SfxTemplate> relevantTemplates = catalog.ForGun(gun);
		if (relevantTemplates.Count == 0)
		{
			throw new InvalidOperationException("Unsupported gun or no templates: " + gun);
		}
		List<SfxTemplate> list = relevantTemplates.ToList();
		SpectralFeatures haystack = SpectralFeatures.Create(clipAudio);
		List<Match> list2 = new List<Match>();
		foreach (SfxTemplate item in list)
		{
			MonoAudio audio = AudioLoader.LoadMono(item.FullPath(sfxRoot));
			audio = Resample(audio, clipAudio.SampleRate);
			double value = item.ConfirmationOffsetSeconds.Value;
			double num = Math.Max(0.0, value - 0.02);
			MonoAudio audio2 = Slice(audio, num, value + 0.45);
			SpectralFeatures needle = SpectralFeatures.Create(audio2);
			double templatePeakDelaySeconds = Math.Max(0.0, FindTemplatePeakTime(audio) - value);
			list2.AddRange(MatchTemplate(haystack, needle, item, value - num, templatePeakDelaySeconds));
		}
		list2 = SnapToStrongTransients(list2, clipAudio, gun);
		List<Match> negatives = list2.Where((Match m) => m.Template.Type == ShotOutcome.Bolt || m.Template.Type == ShotOutcome.Reload).ToList();
		List<Match> source = (from m in list2
			where m.Template.Type != ShotOutcome.Bolt && m.Template.Type != ShotOutcome.Reload
			where m.Score >= 0.76
			where !negatives.Any((Match n) => Math.Abs(n.AnchorSeconds - m.AnchorSeconds) <= 0.25 && n.Score >= m.Score)
			where !m.Supplemental || m.Score - (from n in negatives
				where Math.Abs(n.TransientPeakSeconds - m.TransientPeakSeconds) <= 0.001
				select n.Score).DefaultIfEmpty(0.0).Max() >= 0.13
			orderby m.Score descending
			select m).ToList();
		if (string.Equals(GunNameNormalizer.Resolve(gun), "MORS", StringComparison.OrdinalIgnoreCase))
		{
			Match sequenceStart = (from m in source
				where m.GunLikeness >= 0.65
				orderby m.TransientPeakSeconds
				select m).FirstOrDefault();
			if (sequenceStart != null)
			{
				source = source.Where((Match m) => m.TransientPeakSeconds >= sequenceStart.TransientPeakSeconds - 0.001).ToList();
			}
		}
		List<Match> source2 = (from m in source
			group m by Math.Round(m.AnchorSeconds, 3) into g
			select g.OrderByDescending((Match m) => m.Score).First()).ToList();
		List<Match> list3 = new List<Match>();
		foreach (Match match in source2.OrderByDescending(MatchQuality))
		{
			if (!list3.Any((Match m) => Conflicts(m, match, gun, relevantTemplates)))
			{
				list3.Add(match);
			}
		}
		return list3.OrderBy((Match m) => m.AnchorSeconds).Select(delegate(Match m)
		{
			double num2 = FindLocalAttackOnset(clipAudio, m.TransientPeakSeconds);
			return new ShotEvent
			{
				SourceMuzzleTimeSeconds = num2,
				SourceConfirmationTimeSeconds = num2,
				Outcome = m.Template.Type,
				Confidence = m.Score,
				TemplateId = m.Template.Id,
				ReviewState = ShotReviewState.Candidate
			};
		}).ToList();
	}

	private static bool IsKill(ShotOutcome outcome)
	{
		return outcome == ShotOutcome.Hit || outcome == ShotOutcome.Headshot;
	}

	private static double MatchQuality(Match match)
	{
		return match.Score + 0.1 * match.GunLikeness;
	}

	private static double MinimumShotInterval(string gun, IReadOnlyList<SfxTemplate> templates)
	{
		string a = GunNameNormalizer.Resolve(gun);
		if (string.Equals(a, "MORS", StringComparison.OrdinalIgnoreCase))
		{
			return 0.3;
		}
		return templates.Any((SfxTemplate t) => t.Type == ShotOutcome.Bolt) ? 0.7 : 0.3;
	}

	private static bool Conflicts(Match a, Match b, string gun, IReadOnlyList<SfxTemplate> templates)
	{
		double num = Math.Abs(a.AnchorSeconds - b.AnchorSeconds);
		double num2 = MinimumShotInterval(gun, templates);
		if (num < num2)
		{
			return true;
		}
		string a2 = GunNameNormalizer.Resolve(gun);
		if (string.Equals(a2, "MORS", StringComparison.OrdinalIgnoreCase) && num < 0.9 && (a.GunLikeness < 0.65 || b.GunLikeness < 0.65))
		{
			return true;
		}
		return false;
	}

	private static IEnumerable<Match> MatchTemplate(SpectralFeatures haystack, SpectralFeatures needle, SfxTemplate template, double anchorOffsetSeconds, double templatePeakDelaySeconds)
	{
		List<Match> list = new List<Match>();
		if (needle.FrameCount < 1 || needle.FrameCount > haystack.FrameCount)
		{
			return list;
		}
		int num = Math.Max(1, needle.FrameCount / 12);
		for (int i = 0; i <= haystack.FrameCount - needle.FrameCount; i++)
		{
			double num2 = Correlation(haystack, i, needle);
			if (!(num2 < 0.68))
			{
				double num3 = (double)i * haystack.HopSeconds;
				list.Add(new Match
				{
					StartSeconds = num3,
					AnchorSeconds = num3 + anchorOffsetSeconds,
					TemplateAlignedOnsetSeconds = num3 + anchorOffsetSeconds,
					Score = num2,
					Template = template,
					TemplatePeakDelaySeconds = templatePeakDelaySeconds
				});
				i += num;
			}
		}
		return list;
	}

	private static List<Match> SnapToStrongTransients(List<Match> matches, MonoAudio audio, string gun)
	{
		List<Transient> source = FindStrongTransients(audio, gun);
		List<Match> list = new List<Match>();
		foreach (Match match in matches)
		{
			Transient transient = source.OrderBy((Transient t) => Math.Abs(t.TimeSeconds - match.AnchorSeconds)).FirstOrDefault();
			if (transient != null && !(Math.Abs(transient.TimeSeconds - match.AnchorSeconds) > 0.18))
			{
				match.AnchorSeconds = ((match.Template.Type == ShotOutcome.Hit || match.Template.Type == ShotOutcome.Headshot) ? Math.Max(0.0, transient.TimeSeconds - match.TemplatePeakDelaySeconds) : transient.TimeSeconds);
				match.TransientStrength = transient.Strength;
				match.GunLikeness = transient.GunLikeness;
				match.TransientPeakSeconds = transient.TimeSeconds;
				match.Supplemental = transient.Supplemental;
				list.Add(match);
			}
		}
		return list;
	}

	private static double FindLocalAttackOnset(MonoAudio audio, double peakSeconds)
	{
		int num = Math.Max(1, (int)Math.Round((double)audio.SampleRate * 0.005));
		int num2 = Math.Max(1, (int)Math.Round((double)audio.SampleRate * 0.001));
		int num3 = Math.Max(0, (int)Math.Round((peakSeconds - 0.35) * (double)audio.SampleRate));
		int num4 = Math.Min(audio.Samples.Length - num, (int)Math.Round((peakSeconds + 0.01) * (double)audio.SampleRate));
		if (num4 <= num3)
		{
			return Math.Max(0.0, peakSeconds);
		}
		int num5 = 1 + (num4 - num3) / num2;
		double[] array = new double[num5];
		for (int i = 0; i < num5; i++)
		{
			int num6 = num3 + i * num2;
			double num7 = 0.0;
			for (int j = 0; j < num; j++)
			{
				double num8 = audio.Samples[num6 + j];
				num7 += num8 * num8;
			}
			array[i] = Math.Sqrt(num7 / (double)num);
		}
		int num9 = Math.Max(0, Math.Min(num5 - 1, (int)Math.Round((peakSeconds * (double)audio.SampleRate - (double)num3) / (double)num2)));
		double num10 = array.Take(num9 + 1).Max();
		double[] array2 = (from v in array.Take(Math.Max(1, num9 - 50))
			orderby v
			select v).ToArray();
		double num11 = array2[Math.Min(array2.Length - 1, array2.Length / 5)];
		int num12 = Math.Max(1, (int)Math.Round(0.012 * (double)audio.SampleRate / (double)num2));
		double num13 = 0.0;
		int num14 = -1;
		for (int num15 = num12 + 1; num15 < num9 - 1; num15++)
		{
			double num16 = (double)((num9 - num15) * num2) / (double)audio.SampleRate;
			if (num16 < 0.12 || num16 > 0.33)
			{
				continue;
			}
			double num17 = array[num15] - array[num15 - num12];
			if (!(num17 <= array[num15 - 1] - array[Math.Max(0, num15 - 1 - num12)]) && !(num17 < array[num15 + 1] - array[Math.Max(0, num15 + 1 - num12)]) && !(array[num15] > num11 + 0.2 * Math.Max(0.0, num10 - num11)))
			{
				double timeSeconds = (double)(num3 + num15 * num2) / (double)audio.SampleRate;
				ComputeTransientSpectrum(audio, timeSeconds, out var flatness, out var highFrequencyRatio);
				if (!(flatness > 0.065) && !(highFrequencyRatio > 0.08) && num17 > num13)
				{
					num13 = num17;
					num14 = num15;
				}
			}
		}
		if (num14 >= 0)
		{
			return (double)(num3 + num14 * num2) / (double)audio.SampleRate;
		}
		double num18 = num11 + 0.035 * Math.Max(0.0, num10 - num11);
		int num19 = Math.Max(2, (int)Math.Round(0.008 * (double)audio.SampleRate / (double)num2));
		int num20 = 0;
		int val = 0;
		for (int num21 = num9; num21 >= 0; num21--)
		{
			if (array[num21] <= num18)
			{
				num20++;
				if (num20 >= num19)
				{
					val = num21 + num20;
					break;
				}
			}
			else
			{
				num20 = 0;
			}
		}
		int num22 = Math.Max(0, Math.Min(num9, val));
		return (double)(num3 + num22 * num2) / (double)audio.SampleRate;
	}

	private static double FindTemplatePeakTime(MonoAudio audio)
	{
		double hopSeconds;
		float[] array = ComputeRmsEnvelope(audio, out hopSeconds);
		if (array.Length == 0)
		{
			return 0.0;
		}
		int num = 0;
		for (int i = 1; i < array.Length; i++)
		{
			if (array[i] > array[num])
			{
				num = i;
			}
		}
		return (double)num * hopSeconds;
	}

	private static List<Transient> FindStrongTransients(MonoAudio audio, string gun)
	{
		double hopSeconds;
		float[] array = ComputeRmsEnvelope(audio, out hopSeconds);
		if (array.Length < 3)
		{
			return new List<Transient>();
		}
		float[] array2 = (float[])array.Clone();
		Array.Sort(array2);
		double num = array2[array2.Length / 2];
		double maximum = array2[array2.Length - 1];
		double num2 = Math.Max(num * 3.5, maximum * 0.15);
		int num3 = Math.Max(1, (int)Math.Round(0.08 / hopSeconds));
		int num4 = Math.Max(1, (int)Math.Round(0.08 / hopSeconds));
		int num5 = Math.Max(1, (int)Math.Round(0.15 / hopSeconds));
		bool flag = string.Equals(GunNameNormalizer.Resolve(gun), "MORS", StringComparison.OrdinalIgnoreCase);
		List<KeyValuePair<double, float>> list = new List<KeyValuePair<double, float>>();
		for (int i = 1; i < array.Length - 1; i++)
		{
			if ((double)array[i] < num2 || array[i] < array[i - 1] || array[i] < array[i + 1])
			{
				continue;
			}
			float num6 = array[Math.Max(0, i - num3)];
			if ((double)(array[i] - num6) < (double)array[i] * 0.2)
			{
				continue;
			}
			float num7 = array[Math.Min(array.Length - 1, i + num5)];
			if (!((double)(array[i] - num7) < (double)array[i] * 0.25))
			{
				float num8 = array[Math.Min(array.Length - 1, i + num4)];
				if (!flag || !((double)(array[i] - num8) > (double)array[i] * 0.7))
				{
					list.Add(new KeyValuePair<double, float>((double)i * hopSeconds, array[i]));
				}
			}
		}
		List<KeyValuePair<double, float>> list2 = new List<KeyValuePair<double, float>>();
		foreach (KeyValuePair<double, float> candidate in list.OrderByDescending((KeyValuePair<double, float> c) => c.Value))
		{
			if (!list2.Any((KeyValuePair<double, float> s) => Math.Abs(s.Key - candidate.Key) < 0.4))
			{
				list2.Add(candidate);
			}
		}
		List<Transient> list3 = list2.Select((KeyValuePair<double, float> c) => CreateTransient(audio, c.Key, c.Value / Math.Max((float)maximum, 1E-09f))).ToList();
		if (flag && list2.Count > 1)
		{
			double num9 = Math.Max(num * 1.35, maximum * 0.08);
			List<KeyValuePair<double, float>> list4 = list2.OrderBy((KeyValuePair<double, float> s) => s.Key).ToList();
			for (int num10 = 0; num10 + 1 < list4.Count; num10++)
			{
				double key = list4[num10].Key;
				double key2 = list4[num10 + 1].Key;
				if (key2 - key < 2.5)
				{
					continue;
				}
				List<KeyValuePair<double, float>> list5 = new List<KeyValuePair<double, float>>();
				for (int num11 = 1; num11 < array.Length - 1; num11++)
				{
					double num12 = (double)num11 * hopSeconds;
					if (!(num12 < key + 0.3) && !(num12 > key2 - 0.3) && !((double)array[num11] < num9) && !(array[num11] < array[num11 - 1]) && !(array[num11] < array[num11 + 1]))
					{
						list5.Add(new KeyValuePair<double, float>(num12, array[num11]));
					}
				}
				foreach (KeyValuePair<double, float> candidate2 in list5.OrderByDescending((KeyValuePair<double, float> c) => c.Value))
				{
					if (!list3.Any((Transient t) => Math.Abs(t.TimeSeconds - candidate2.Key) < 0.3))
					{
						Transient transient = CreateTransient(audio, candidate2.Key, candidate2.Value / Math.Max((float)maximum, 1E-09f));
						transient.Supplemental = true;
						list3.Add(transient);
					}
				}
			}
		}
		return list3.OrderBy((Transient t) => t.TimeSeconds).ToList();
	}

	private static Transient CreateTransient(MonoAudio audio, double timeSeconds, double strength)
	{
		ComputeTransientSpectrum(audio, timeSeconds, out var flatness, out var highFrequencyRatio);
		double num = Math.Max(0.0, Math.Min(1.0, flatness / 0.06));
		double num2 = Math.Max(0.0, Math.Min(1.0, highFrequencyRatio / 0.07));
		return new Transient
		{
			TimeSeconds = timeSeconds,
			Strength = strength,
			GunLikeness = 0.6 * num + 0.4 * num2
		};
	}

	private static double Correlation(SpectralFeatures haystack, int startFrame, SpectralFeatures needle)
	{
		int num = startFrame * 32;
		double num2 = 0.0;
		double num3 = 0.0;
		double num4 = 0.0;
		for (int i = 0; i < needle.Values.Length; i++)
		{
			double num5 = needle.FrameWeights[i / 32];
			num2 += num5;
			num3 += num5 * (double)haystack.Values[num + i];
			num4 += num5 * (double)needle.Values[i];
		}
		if (num2 <= 1E-12)
		{
			return 0.0;
		}
		num3 /= num2;
		num4 /= num2;
		double num6 = 0.0;
		double num7 = 0.0;
		double num8 = 0.0;
		for (int j = 0; j < needle.Values.Length; j++)
		{
			double num9 = needle.FrameWeights[j / 32];
			double num10 = (double)haystack.Values[num + j] - num3;
			double num11 = (double)needle.Values[j] - num4;
			num6 += num9 * num10 * num11;
			num7 += num9 * num10 * num10;
			num8 += num9 * num11 * num11;
		}
		double num12 = ((num7 <= 1E-12 || num8 <= 1E-12) ? 0.0 : (num6 / Math.Sqrt(num7 * num8)));
		double num13 = 0.0;
		double num14 = 0.0;
		double num15 = 0.0;
		for (int k = 0; k < needle.FrameCount; k++)
		{
			double num16 = haystack.EnergyEnvelope[startFrame + k];
			double num17 = needle.EnergyEnvelope[k];
			num13 += num16 * num17;
			num14 += num16 * num16;
			num15 += num17 * num17;
		}
		double num18 = ((num14 <= 1E-12 || num15 <= 1E-12) ? 0.0 : (num13 / Math.Sqrt(num14 * num15)));
		return 0.65 * num12 + 0.35 * num18;
	}

	private static MonoAudio Resample(MonoAudio audio, int sampleRate)
	{
		if (audio.SampleRate == sampleRate)
		{
			return audio;
		}
		int num = (int)Math.Round((double)audio.Samples.Length * (double)sampleRate / (double)audio.SampleRate);
		float[] array = new float[num];
		for (int i = 0; i < num; i++)
		{
			double num2 = (double)i * (double)audio.SampleRate / (double)sampleRate;
			int num3 = Math.Min(audio.Samples.Length - 1, (int)num2);
			int num4 = Math.Min(audio.Samples.Length - 1, num3 + 1);
			double num5 = num2 - (double)num3;
			array[i] = (float)((double)audio.Samples[num3] * (1.0 - num5) + (double)audio.Samples[num4] * num5);
		}
		return new MonoAudio
		{
			Samples = array,
			SampleRate = sampleRate
		};
	}

	private static MonoAudio Slice(MonoAudio audio, double startSeconds, double endSeconds)
	{
		int num = Math.Max(0, Math.Min(audio.Samples.Length, (int)Math.Floor(startSeconds * (double)audio.SampleRate)));
		int num2 = Math.Max(num, Math.Min(audio.Samples.Length, (int)Math.Ceiling(endSeconds * (double)audio.SampleRate)));
		float[] array = new float[num2 - num];
		Array.Copy(audio.Samples, num, array, 0, array.Length);
		return new MonoAudio
		{
			Samples = array,
			SampleRate = audio.SampleRate
		};
	}

	internal static float[] ComputeRmsEnvelope(MonoAudio audio, out double hopSeconds)
	{
		int num = Math.Max(1, (int)((double)audio.SampleRate * 0.04));
		int num2 = Math.Max(1, (int)((double)audio.SampleRate * 0.01));
		hopSeconds = (double)num2 / (double)audio.SampleRate;
		int num3 = Math.Max(0, (audio.Samples.Length - num) / num2);
		float[] array = new float[num3];
		for (int i = 0; i < num3; i++)
		{
			double num4 = 0.0;
			for (int j = 0; j < num; j++)
			{
				double num5 = audio.Samples[i * num2 + j];
				num4 += num5 * num5;
			}
			array[i] = (float)Math.Sqrt(num4 / (double)num);
		}
		return array;
	}

	internal static void ComputeTransientSpectrum(MonoAudio audio, double timeSeconds, out double flatness, out double highFrequencyRatio)
	{
		int num = (int)Math.Round(timeSeconds * (double)audio.SampleRate);
		int num2 = Math.Max(0, Math.Min(audio.Samples.Length - 1024, num - 512));
		if (audio.Samples.Length < 1024)
		{
			flatness = 0.0;
			highFrequencyRatio = 0.0;
			return;
		}
		double num3 = 0.0;
		double num4 = 0.0;
		double num5 = 0.0;
		int num6 = 0;
		for (int i = 1; i < 512; i++)
		{
			double num7 = (double)i * (double)audio.SampleRate / 1024.0;
			if (!(num7 < 200.0) && !(num7 > 16000.0))
			{
				double num8 = 0.0;
				double num9 = 0.0;
				for (int j = 0; j < 1024; j++)
				{
					double num10 = 0.5 - 0.5 * Math.Cos(Math.PI * 2.0 * (double)j / 1023.0);
					double num11 = (double)audio.Samples[num2 + j] * num10;
					double num12 = Math.PI * 2.0 * (double)i * (double)j / 1024.0;
					num8 += num11 * Math.Cos(num12);
					num9 -= num11 * Math.Sin(num12);
				}
				double num13 = Math.Max(1E-12, num8 * num8 + num9 * num9);
				num4 += num13;
				if (num7 >= 4000.0)
				{
					num5 += num13;
				}
				num3 += Math.Log(num13);
				num6++;
			}
		}
		double num14 = num4 / (double)Math.Max(1, num6);
		flatness = ((num14 <= 0.0) ? 0.0 : (Math.Exp(num3 / (double)Math.Max(1, num6)) / num14));
		highFrequencyRatio = num5 / Math.Max(num4, 1E-12);
	}
}
