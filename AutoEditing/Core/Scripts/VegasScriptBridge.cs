using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Core.Domain.Audio;
using Core.Domain.Editing;
using Newtonsoft.Json;
using ScriptPortal.Vegas;

namespace Core.Scripts;

internal static class VegasScriptBridge
{
	private sealed class Request
	{
		public string Action { get; set; }

		public ShotReviewWorkflow.AnalysisBatch Analysis { get; set; }

		public MontageOrchestrator.PreparedMontage Montage { get; set; }

		public string SongPath { get; set; }

		public string ClipsFolder { get; set; }

		public string SfxRoot { get; set; }

		public int ClipIndex { get; set; }

		public string Status { get; set; }

		public string Error { get; set; }
	}

	private const string AnalysisAction = "LayoutAnalysis";

	private const string MontageAction = "BuildMontage";

	private const string LayoutSingleAction = "LayoutSingleClip";

	private const string RemoveClipAction = "RemoveClipFromTimeline";

	private const string MarkReadyAction = "MarkClipReady";

	public static void LayoutAnalysis(Vegas vegas, ShotReviewWorkflow.AnalysisBatch batch)
	{
		Run(vegas, new Request
		{
			Action = "LayoutAnalysis",
			Analysis = batch
		});
	}

	public static void BuildMontage(Vegas vegas, MontageOrchestrator.PreparedMontage montage, string songPath)
	{
		Run(vegas, new Request
		{
			Action = "BuildMontage",
			Montage = montage,
			SongPath = songPath
		});
	}

	public static void LayoutSingleClip(Vegas vegas, ShotReviewWorkflow.AnalysisItem item)
	{
		Run(vegas, new Request { Action = LayoutSingleAction, Analysis = new ShotReviewWorkflow.AnalysisBatch { Items = new System.Collections.Generic.List<ShotReviewWorkflow.AnalysisItem> { item } } });
	}

	public static void RemoveClipFromTimeline(Vegas vegas, int clipIndex)
	{
		Run(vegas, new Request { Action = RemoveClipAction, ClipIndex = clipIndex });
	}

	public static void MarkClipReady(Vegas vegas, string clipsFolder, string sfxRoot, int clipIndex)
	{
		Run(vegas, new Request { Action = MarkReadyAction, ClipsFolder = clipsFolder, SfxRoot = sfxRoot, ClipIndex = clipIndex });
	}

	public static bool TryExecutePending(Vegas vegas)
	{
		string requestPath = GetRequestPath();
		if (!File.Exists(requestPath))
		{
			return false;
		}
		Request request = Read(requestPath);
		request.Status = "Running";
		Write(requestPath, request);
		try
		{
			if (request.Action == "LayoutAnalysis")
			{
				if (request.Analysis == null)
				{
					throw new InvalidOperationException("Analysis layout request is empty.");
				}
				new ShotReviewWorkflow().LayOutAnalysis(vegas, request.Analysis, null, CancellationToken.None);
			}
			else if (request.Action == LayoutSingleAction)
			{
				if (request.Analysis == null || request.Analysis.Items.Count != 1) throw new InvalidOperationException("Single clip layout request is empty.");
				new ShotReviewWorkflow().LayOutSingleClip(vegas, request.Analysis.Items[0]);
			}
			else if (request.Action == RemoveClipAction)
			{
				new ShotReviewWorkflow().RemoveClipFromTimeline(vegas, request.ClipIndex);
			}
			else if (request.Action == MarkReadyAction)
			{
				new ShotReviewWorkflow().MarkClipReady(vegas, request.ClipsFolder, request.SfxRoot, request.ClipIndex);
			}
			else
			{
				if (!(request.Action == "BuildMontage"))
				{
					throw new InvalidOperationException("Unknown VEGAS script bridge action: " + request.Action);
				}
				if (request.Montage == null)
				{
					throw new InvalidOperationException("Montage build request is empty.");
				}
				ShotReviewWorkflow.CleanupGenerated(vegas);
				new MontageOrchestrator().BuildPreparedMontage(vegas, request.Montage, request.SongPath, applyEffects: true);
			}
			request.Status = "Completed";
			request.Error = null;
			Write(requestPath, request);
			return true;
		}
		catch (Exception ex)
		{
			request.Status = "Failed";
			request.Error = ex.ToString();
			Write(requestPath, request);
			throw;
		}
	}

	private static void Run(Vegas vegas, Request request)
	{
		string location = Assembly.GetExecutingAssembly().Location;
		string requestPath = GetRequestPath();
		request.Status = "Pending";
		Write(requestPath, request);
		try
		{
			vegas.RunScriptFile(location);
			Request request2 = Read(requestPath);
			if (request2.Status != "Completed")
			{
				throw new InvalidOperationException("VEGAS script mutation failed. " + (request2.Error ?? "No completion status was returned."));
			}
		}
		finally
		{
			try
			{
				File.Delete(requestPath);
			}
			catch
			{
			}
		}
	}

	private static string GetRequestPath()
	{
		return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "AutoEditing.vegas-request.json");
	}

	private static Request Read(string path)
	{
		return JsonConvert.DeserializeObject<Request>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Could not read the VEGAS script bridge request.");
	}

	private static void Write(string path, Request request)
	{
		File.WriteAllText(path, JsonConvert.SerializeObject((object)request, (Formatting)1));
	}
}
