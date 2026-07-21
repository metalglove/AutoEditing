using System;
using System.IO;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;

namespace AnalysisHarness
{
	internal static class SongAnalysisSelfTests
	{
		public static void RunAll()
		{
			string directory = Path.Combine(Path.GetTempPath(), "AutoEditing-SongAnalysis-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				TestBeatGridAdaptation(directory);
				TestPersistenceRoundTrip(directory);
				TestUnknownSchemaIsRejected(directory);
				TestReanalysisPreservesReviewedEdits(directory);
				Console.WriteLine("Song-analysis self-tests passed.");
			}
			finally
			{
				if (Directory.Exists(directory))
				{
					Directory.Delete(directory, true);
				}
			}
		}

		private static void TestBeatGridAdaptation(string directory)
		{
			SongIdentity identity = CreateIdentity(directory);
			BeatGrid grid = new BeatGrid { Bpm = 120.0, FirstBeatOffsetSeconds = 0.5 };
			grid.BeatTimesSeconds.AddRange(new[] { 0.5, 1.0, 1.5 });
			SongAnalysis first = BeatGridSongAnalysisAdapter.Create(grid, identity);
			SongAnalysis second = BeatGridSongAnalysisAdapter.Create(grid, identity);
			Assert(first.Events.Count == 3, "BeatGrid adaptation lost events.");
			Assert(first.Events[1].Id == second.Events[1].Id, "BeatGrid adaptation did not produce stable IDs.");
			Assert(first.Events.All((MusicEvent item) => item.Origin == MusicAnalysisOrigin.Detected), "Adapted beats have the wrong origin.");
		}

		private static void TestPersistenceRoundTrip(string directory)
		{
			SongIdentity identity = CreateIdentity(directory);
			BeatGrid grid = new BeatGrid { Bpm = 100.0, FirstBeatOffsetSeconds = 0.25 };
			grid.BeatTimesSeconds.Add(0.25);
			SongAnalysis analysis = BeatGridSongAnalysisAdapter.Create(grid, identity);
			analysis.Events[0].Type = MusicEventType.Downbeat;
			analysis.Events[0].ReviewState = MusicAnalysisReviewState.Reviewed;
			analysis.Events[0].Editorial = new EditorialMetadata { IsLocked = true, Priority = 8, Notes = "Opening accent" };
			analysis.Regions.Add(new MusicRegion
			{
				Id = "region-intro",
				StartSeconds = 0.0,
				EndSeconds = 8.0,
				Type = MusicRegionType.Intro,
				Origin = MusicAnalysisOrigin.UserCreated,
				ReviewState = MusicAnalysisReviewState.Reviewed
			});
			string path = Path.Combine(directory, "analysis.json");
			SongAnalysisStore store = new SongAnalysisStore();
			store.Save(path, analysis);
			SongAnalysis loaded = store.Load(path);
			Assert(loaded.Id == analysis.Id, "Analysis ID did not round-trip.");
			Assert(loaded.Events[0].Type == MusicEventType.Downbeat, "Event type did not round-trip.");
			Assert(loaded.Events[0].DetectedType == MusicEventType.Beat, "Detector provenance was discarded.");
			Assert(loaded.Events[0].Editorial.Notes == "Opening accent", "Editorial metadata did not round-trip.");
			Assert(loaded.Regions.Single().Type == MusicRegionType.Intro, "Region did not round-trip.");
		}

		private static void TestUnknownSchemaIsRejected(string directory)
		{
			SongIdentity identity = CreateIdentity(directory);
			SongAnalysis analysis = new SongAnalysis { Song = identity, SchemaVersion = SongAnalysis.CurrentSchemaVersion + 1 };
			string path = Path.Combine(directory, "future.json");
			File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(analysis));
			bool rejected = false;
			try
			{
				new SongAnalysisStore().Load(path);
			}
			catch (NotSupportedException)
			{
				rejected = true;
			}
			Assert(rejected, "A future schema version was accepted.");
		}

		private static void TestReanalysisPreservesReviewedEdits(string directory)
		{
			SongIdentity identity = CreateIdentity(directory);
			BeatGrid firstGrid = new BeatGrid { Bpm = 120.0, FirstBeatOffsetSeconds = 1.0 };
			firstGrid.BeatTimesSeconds.Add(1.0);
			SongAnalysis existing = BeatGridSongAnalysisAdapter.Create(firstGrid, identity);
			string originalId = existing.Events[0].Id;
			existing.Events[0].TimeSeconds = 1.04;
			existing.Events[0].Type = MusicEventType.Downbeat;
			existing.Events[0].ReviewState = MusicAnalysisReviewState.Reviewed;
			existing.Events[0].Editorial.IsLocked = true;
			existing.Events.Add(new MusicEvent
			{
				Id = "manual-event",
				TimeSeconds = 2.0,
				Type = MusicEventType.ManualSyncPoint,
				Origin = MusicAnalysisOrigin.UserCreated,
				ReviewState = MusicAnalysisReviewState.Reviewed
			});
			existing.Events.Add(new MusicEvent
			{
				Id = "reviewed-missed-event",
				TimeSeconds = 3.0,
				Type = MusicEventType.Accent,
				DetectedTimeSeconds = 3.0,
				DetectedType = MusicEventType.Accent,
				Origin = MusicAnalysisOrigin.Detected,
				ReviewState = MusicAnalysisReviewState.Reviewed
			});

			BeatGrid secondGrid = new BeatGrid { Bpm = 120.0, FirstBeatOffsetSeconds = 1.03 };
			secondGrid.BeatTimesSeconds.Add(1.03);
			SongAnalysis detected = BeatGridSongAnalysisAdapter.Create(secondGrid, identity);
			SongAnalysis reconciled = new SongAnalysisReconciler().Reconcile(existing, detected);
			MusicEvent reviewed = reconciled.Events.Single((MusicEvent item) => item.Id == originalId);
			Assert(Math.Abs(reviewed.TimeSeconds - 1.04) < 0.0001, "Reviewed event time was overwritten.");
			Assert(reviewed.Type == MusicEventType.Downbeat, "Reviewed event classification was overwritten.");
			Assert(Math.Abs(reviewed.DetectedTimeSeconds.Value - 1.03) < 0.0001, "New detector provenance was not retained.");
			Assert(reconciled.Events.Any((MusicEvent item) => item.Id == "manual-event"), "Manual event was lost during reconciliation.");
			Assert(reconciled.Events.Any((MusicEvent item) => item.Id == "reviewed-missed-event"), "A reviewed event disappeared when re-analysis missed it.");
		}

		private static SongIdentity CreateIdentity(string directory)
		{
			string path = Path.Combine(directory, "song.bin");
			if (!File.Exists(path))
			{
				File.WriteAllBytes(path, new byte[] { 1, 3, 3, 7 });
			}
			return SongIdentity.FromFile(path, 180.0);
		}

		private static void Assert(bool condition, string message)
		{
			if (!condition)
			{
				throw new InvalidOperationException(message);
			}
		}
	}
}
