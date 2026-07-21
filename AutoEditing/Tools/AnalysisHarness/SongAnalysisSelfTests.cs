using System;
using System.Collections.Generic;
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
				TestSilenceAndShortAudio();
				TestStructuredRhythmAnalysis();
				TestReviewedIdWinsReanalysisCollision();
				TestMatchedIdCannotCollideWithNewProposal();
				TestEditorialAssignmentsRoundTripAndValidate(directory);
				TestEditorialValidationBoundaries();
				TestLockedEditorialDecisionSurvivesReanalysis();
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

		private static void TestSilenceAndShortAudio()
		{
			SongStructureAnalyzer analyzer = new SongStructureAnalyzer();
			MonoAudio silence = new MonoAudio { SampleRate = 8000, Samples = new float[16000] };
			SongAnalysis silentAnalysis = analyzer.Analyze(silence, SyntheticIdentity("silence", silence.DurationSeconds));
			Assert(silentAnalysis.Events.Count == 0, "Silence produced invented musical events.");
			Assert(silentAnalysis.Regions.Count == 1 && silentAnalysis.Regions[0].Type == MusicRegionType.Unused, "Silence was not classified as unused.");

			MonoAudio shortAudio = new MonoAudio { SampleRate = 8000, Samples = new float[2000] };
			shortAudio.Samples[100] = 1.0f;
			SongAnalysis shortAnalysis = analyzer.Analyze(shortAudio, SyntheticIdentity("short", shortAudio.DurationSeconds));
			Assert(!shortAnalysis.TempoBpm.HasValue, "A very short track produced an unreliable tempo.");
		}

		private static void TestStructuredRhythmAnalysis()
		{
			const int sampleRate = 44100;
			const double duration = 24.0;
			float[] samples = new float[(int)(sampleRate * duration)];
			int beatCount = (int)(duration / 0.5);
			for (int beat = 0; beat < beatCount; beat++)
			{
				double time = 0.25 + beat * 0.5;
				int start = (int)(time * sampleRate);
				double sectionGain = time < 8.0 ? 0.25 : time < 16.0 ? 0.55 : 1.0;
				double beatGain = beat % 4 == 0 ? 1.0 : 0.45;
				for (int offset = 0; offset < 700 && start + offset < samples.Length; offset++)
				{
					samples[start + offset] = (float)(sectionGain * beatGain * Math.Sin(offset * 0.12) * (1.0 - offset / 700.0));
				}
			}
			MonoAudio audio = new MonoAudio { SampleRate = sampleRate, Samples = samples };
			SongAnalysis analysis = new SongStructureAnalyzer().Analyze(audio, SyntheticIdentity("rhythm", duration));
			Assert(analysis.TempoBpm.HasValue, "Rhythmic audio produced no tempo.");
			Assert(analysis.Events.Any((MusicEvent item) => item.Type == MusicEventType.Beat), "Rhythmic audio produced no beats.");
			Assert(analysis.Events.Any((MusicEvent item) => item.Type == MusicEventType.Downbeat), "Rhythmic audio produced no downbeat proposals.");
			Assert(analysis.Events.All((MusicEvent item) => item.Confidence.HasValue && item.Strength.HasValue), "Detected events are missing evidence scores.");
			Assert(analysis.Regions.Count > 1, "Energy changes produced no candidate song sections.");
			Assert(Math.Abs(analysis.Regions[0].StartSeconds) < 0.0001, "Candidate regions do not start at zero.");
			Assert(Math.Abs(analysis.Regions.Last().EndSeconds - duration) < 0.0001, "Candidate regions do not cover the song end.");
			for (int index = 1; index < analysis.Regions.Count; index++)
			{
				Assert(Math.Abs(analysis.Regions[index - 1].EndSeconds - analysis.Regions[index].StartSeconds) < 0.0001, "Candidate regions overlap or contain a gap.");
			}
		}

		private static SongIdentity SyntheticIdentity(string fingerprint, double duration)
		{
			return new SongIdentity { ContentFingerprint = fingerprint, LastKnownPath = fingerprint, DurationSeconds = duration };
		}

		private static void TestReviewedIdWinsReanalysisCollision()
		{
			SongIdentity identity = SyntheticIdentity("collision", 60.0);
			MusicEvent reviewed = new MusicEvent
			{
				Id = "stable-phrase-id",
				TimeSeconds = 10.0,
				DetectedTimeSeconds = 10.0,
				Type = MusicEventType.PhraseBoundary,
				DetectedType = MusicEventType.PhraseBoundary,
				Origin = MusicAnalysisOrigin.Detected,
				ReviewState = MusicAnalysisReviewState.Reviewed
			};
			SongAnalysis existing = new SongAnalysis { Song = identity, Events = new List<MusicEvent> { reviewed } };
			MusicEvent shiftedProposal = new MusicEvent
			{
				Id = "stable-phrase-id",
				TimeSeconds = 30.0,
				DetectedTimeSeconds = 30.0,
				Type = MusicEventType.PhraseBoundary,
				DetectedType = MusicEventType.PhraseBoundary,
				Origin = MusicAnalysisOrigin.Detected,
				ReviewState = MusicAnalysisReviewState.Proposed
			};
			SongAnalysis detected = new SongAnalysis { Song = identity, Events = new List<MusicEvent> { shiftedProposal } };
			SongAnalysis reconciled = new SongAnalysisReconciler().Reconcile(existing, detected);
			Assert(reconciled.Events.Count((MusicEvent item) => item.Id == "stable-phrase-id") == 1, "Re-analysis produced duplicate deterministic event IDs.");
			Assert(reconciled.Events.Single().ReviewState == MusicAnalysisReviewState.Reviewed && Math.Abs(reconciled.Events.Single().TimeSeconds - 10.0) < 0.001, "Reviewed event did not win an ID collision.");
		}

		private static void TestMatchedIdCannotCollideWithNewProposal()
		{
			SongIdentity identity = SyntheticIdentity("matched-collision", 60.0);
			MusicEvent oldProposal = DetectedEvent("inherited-id", 20.0);
			SongAnalysis existing = new SongAnalysis { Song = identity, Events = new List<MusicEvent> { oldProposal } };
			MusicEvent matchingProposal = DetectedEvent("new-id", 20.01);
			MusicEvent deterministicCollision = DetectedEvent("inherited-id", 40.0);
			SongAnalysis detected = new SongAnalysis { Song = identity, Events = new List<MusicEvent> { matchingProposal, deterministicCollision } };
			SongAnalysis reconciled = new SongAnalysisReconciler().Reconcile(existing, detected);
			Assert(reconciled.Events.Count == 2, "A legitimate proposal was discarded while repairing an inherited ID collision.");
			Assert(reconciled.Events.Select((MusicEvent item) => item.Id).Distinct(StringComparer.Ordinal).Count() == 2, "Matching produced duplicate event IDs.");
		}

		private static void TestEditorialAssignmentsRoundTripAndValidate(string directory)
		{
			SongIdentity identity = CreateIdentity(directory);
			MusicEvent musicEvent = DetectedEvent("editorial-event", 12.0);
			musicEvent.Editorial = new EditorialMetadata
			{
				Priority = 7,
				IsLocked = true,
				TimingOffsetSeconds = -0.04,
				Intensity = 0.8,
				Notes = "Kill plus flash",
				AllowedUses = new List<EditorialUse> { EditorialUse.GameplayAnchor, EditorialUse.Flash },
				Assignments = new List<EditorialAssignment>
				{
					new EditorialAssignment { Use = EditorialUse.GameplayAnchor, Origin = EditorialAssignmentOrigin.UserChosen },
					new EditorialAssignment { Use = EditorialUse.Flash, Origin = EditorialAssignmentOrigin.UserChosen }
				}
			};
			SongAnalysis analysis = new SongAnalysis { Song = identity, Events = new List<MusicEvent> { musicEvent } };
			string path = Path.Combine(directory, "editorial.json");
			SongAnalysisStore store = new SongAnalysisStore();
			store.Save(path, analysis);
			MusicEvent loaded = store.Load(path).Events.Single();
			Assert(loaded.Editorial.Assignments.Count == 2, "Editorial assignments did not round-trip.");
			Assert(loaded.Editorial.Assignments.All((EditorialAssignment item) => item.Origin == EditorialAssignmentOrigin.UserChosen), "Editorial assignment provenance did not round-trip.");
			Assert(loaded.Editorial.IsLocked && Math.Abs(loaded.Editorial.TimingOffsetSeconds.Value + 0.04) < 0.0001, "Editorial lock or offset did not round-trip.");

			loaded.Editorial.Assignments.Add(new EditorialAssignment { Use = EditorialUse.ScreenPump });
			IReadOnlyList<string> errors = EditorialMetadataValidator.Validate(loaded);
			Assert(errors.Any((string item) => item.Contains("visual accent")), "Conflicting editorial assignments were not explained in plain language.");
		}

		private static void TestEditorialValidationBoundaries()
		{
			MusicEvent musicEvent = DetectedEvent("validation-boundaries", 8.0);
			musicEvent.Editorial = new EditorialMetadata
			{
				Intensity = 0.0,
				TimingOffsetSeconds = -2.0,
				Assignments = new List<EditorialAssignment>
				{
					new EditorialAssignment { Use = EditorialUse.GameplayAnchor },
					new EditorialAssignment { Use = EditorialUse.Flash },
					new EditorialAssignment { Use = EditorialUse.SpeedChange }
				}
			};
			Assert(EditorialMetadataValidator.Validate(musicEvent).Count == 0, "Independent anchor, visual, and timing assignments were rejected.");

			musicEvent.Editorial.Intensity = 1.0;
			musicEvent.Editorial.TimingOffsetSeconds = 2.0;
			Assert(EditorialMetadataValidator.Validate(musicEvent).Count == 0, "Valid inclusive editorial limits were rejected.");

			musicEvent.Editorial.Intensity = 1.01;
			musicEvent.Editorial.TimingOffsetSeconds = -2.01;
			IReadOnlyList<string> rangeErrors = EditorialMetadataValidator.Validate(musicEvent);
			Assert(rangeErrors.Any((string item) => item.Contains("Intensity")), "Out-of-range intensity was accepted.");
			Assert(rangeErrors.Any((string item) => item.Contains("Timing offset")), "Out-of-range timing offset was accepted.");

			musicEvent.Editorial.Intensity = 0.5;
			musicEvent.Editorial.TimingOffsetSeconds = 0.0;
			musicEvent.Editorial.Assignments = new List<EditorialAssignment>
			{
				new EditorialAssignment { Use = EditorialUse.IntentionallyUnused },
				new EditorialAssignment { Use = EditorialUse.Flash }
			};
			Assert(EditorialMetadataValidator.Validate(musicEvent).Any((string item) => item.Contains("cannot be combined")), "Intentionally-unused events accepted an effect assignment.");

			musicEvent.Editorial.Assignments = new List<EditorialAssignment> { new EditorialAssignment { Use = EditorialUse.ScreenPump } };
			musicEvent.Editorial.AllowedUses = new List<EditorialUse> { EditorialUse.Flash };
			Assert(EditorialMetadataValidator.Validate(musicEvent).Any((string item) => item.Contains("allowed uses")), "An assignment outside allowed uses was accepted.");
		}

		private static void TestLockedEditorialDecisionSurvivesReanalysis()
		{
			SongIdentity identity = SyntheticIdentity("locked-editorial", 30.0);
			MusicEvent existingEvent = DetectedEvent("locked-event", 4.0);
			existingEvent.Editorial = new EditorialMetadata
			{
				IsLocked = true,
				Priority = 9,
				Notes = "Keep this treatment",
				Assignments = new List<EditorialAssignment>
				{
					new EditorialAssignment { Use = EditorialUse.GameplayAnchor, Origin = EditorialAssignmentOrigin.UserChosen },
					new EditorialAssignment { Use = EditorialUse.ScreenPump, Origin = EditorialAssignmentOrigin.UserChosen }
				}
			};
			SongAnalysis existing = new SongAnalysis { Song = identity, Events = new List<MusicEvent> { existingEvent } };
			MusicEvent proposal = DetectedEvent("new-detector-id", 4.03);
			proposal.Strength = 0.9;
			SongAnalysis detected = new SongAnalysis { Song = identity, Events = new List<MusicEvent> { proposal } };

			MusicEvent reconciled = new SongAnalysisReconciler().Reconcile(existing, detected).Events.Single();
			Assert(reconciled.Id == "locked-event", "A locked event lost its stable identity during re-analysis.");
			Assert(reconciled.Editorial.IsLocked && reconciled.Editorial.Priority == 9, "Locked editorial settings were overwritten during re-analysis.");
			Assert(reconciled.Editorial.Assignments.Select((EditorialAssignment item) => item.Use).SequenceEqual(new[] { EditorialUse.GameplayAnchor, EditorialUse.ScreenPump }), "Locked editorial assignments were overwritten during re-analysis.");
			Assert(Math.Abs(reconciled.Strength.Value - 0.9) < 0.0001, "Fresh detector evidence was not retained for a locked event.");
		}

		private static MusicEvent DetectedEvent(string id, double time)
		{
			return new MusicEvent
			{
				Id = id,
				TimeSeconds = time,
				DetectedTimeSeconds = time,
				Type = MusicEventType.PhraseBoundary,
				DetectedType = MusicEventType.PhraseBoundary,
				Origin = MusicAnalysisOrigin.Detected,
				ReviewState = MusicAnalysisReviewState.Proposed
			};
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
