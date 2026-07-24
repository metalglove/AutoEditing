using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Domain.Audio;
using Core.Domain.Audio.SongAnalysis;
using Core.Domain.Editing;

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
				TestEffectSelectionOptions();
				TestAutomaticEffectTreatmentRules();
				TestPlacementAwareScreenPumps();
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

		private static void TestAutomaticEffectTreatmentRules()
		{
			AutomaticEffectTreatmentPlanner planner = new AutomaticEffectTreatmentPlanner();
			SongAnalysis manualAnalysis = TreatmentAnalysis(MusicRegionType.Action, 20.0);
			MusicEvent manualDrop = TreatmentEvent("manual-drop", 3.0, MusicEventType.Drop);
			manualDrop.Editorial.Intensity = 0.9;
			manualDrop.Editorial.TimingOffsetSeconds = 0.125;
			manualDrop.Editorial.Assignments.Add(new EditorialAssignment { Use = EditorialUse.Flash, Origin = EditorialAssignmentOrigin.UserChosen });
			manualAnalysis.Events.Add(manualDrop);
			EffectTreatmentPlan manualPlan = planner.Plan(manualAnalysis);
			Assert(manualPlan.Actions.Any(item => item.EventId == manualDrop.Id && item.Type == EditorialUse.Flash && item.Origin == EffectTreatmentOrigin.Manual), "A manual visual treatment was not preserved.");
			Assert(!manualPlan.Actions.Any(item => item.EventId == manualDrop.Id && item.Type == EditorialUse.ScreenPump), "An inferred visual treatment replaced a manual visual override.");
			Assert(manualPlan.Actions.Any(item => item.EventId == manualDrop.Id && item.Type == EditorialUse.SpeedChange), "A manual visual override incorrectly suppressed an independent speed treatment.");
			Assert(Math.Abs(manualPlan.Actions.Single(item => item.EventId == manualDrop.Id && item.Type == EditorialUse.Flash).TimeSeconds - 3.125) < 0.000001, "Editorial timing offset was not applied to a treatment.");

			SongAnalysis suppressedAnalysis = new SongAnalysis
			{
				Song = SyntheticIdentity("effect-suppression", 20.0),
				Regions = new List<MusicRegion>
				{
					TreatmentRegion("before-unused", 0.0, 8.0, MusicRegionType.Action),
					TreatmentRegion("unused-region", 8.0, 12.0, MusicRegionType.Unused),
					TreatmentRegion("after-unused", 12.0, 20.0, MusicRegionType.Action)
				}
			};
			MusicEvent intentionallyUnused = TreatmentEvent("unused-event", 2.0, MusicEventType.Drop);
			intentionallyUnused.Editorial.Assignments.Add(new EditorialAssignment { Use = EditorialUse.IntentionallyUnused, Origin = EditorialAssignmentOrigin.UserChosen });
			suppressedAnalysis.Events.Add(intentionallyUnused);
			suppressedAnalysis.Events.Add(TreatmentEvent("drop-in-unused-region", 10.0, MusicEventType.Drop));
			EffectTreatmentPlan suppressed = planner.Plan(suppressedAnalysis);
			Assert(!suppressed.Actions.Any(item => item.EventId == intentionallyUnused.Id || item.EventId == "drop-in-unused-region"), "An intentionally-unused event or unused song region received an automatic effect.");
			Assert(suppressed.Diagnostics.Any(item => item.Code == "intentionally-unused"), "Intentionally-unused suppression was not diagnosed.");

			SongAnalysis inference = TreatmentAnalysis(MusicRegionType.Action, 40.0);
			inference.Events.Add(TreatmentEvent("drop", 2.0, MusicEventType.Drop));
			inference.Events.Add(TreatmentEvent("build", 8.0, MusicEventType.BuildHit));
			for (int index = 0; index < 16; index++)
				inference.Events.Add(TreatmentEvent("accent-" + index, 14.0 + index * 1.5, MusicEventType.Accent));
			EffectTreatmentPlan inferred = planner.Plan(inference);
			Assert(inferred.Actions.Any(item => item.EventId == "drop" && item.Type == EditorialUse.ScreenPump), "A drop did not infer a screen pump.");
			Assert(inferred.Actions.Any(item => item.EventId == "drop" && item.RecipeId == "native.pump.impact" && item.Intensity >= 0.75 && item.DurationSeconds >= 0.20 && item.DurationSeconds <= 0.30), "A drop did not select the impact pump recipe/intensity/duration tier.");
			Assert(inferred.Actions.Any(item => item.EventId == "drop" && item.Type == EditorialUse.SpeedChange), "A drop did not infer a speed treatment.");
			Assert(inferred.Actions.Any(item => item.EventId == "build" && item.Type == EditorialUse.ScreenPump), "A build hit did not infer a screen pump.");
			Assert(inferred.Actions.Any(item => item.EventId == "build" && item.RecipeId == "native.pump.medium" && item.Intensity >= 0.45 && item.Intensity < 0.75 && item.DurationSeconds >= 0.18 && item.DurationSeconds <= 0.24), "A build hit did not select the medium pump recipe/intensity/duration tier.");
			int accentEffects = inferred.Actions.Count(item => item.EventId != null && item.EventId.StartsWith("accent-", StringComparison.Ordinal));
			Assert(accentEffects > 0 && accentEffects < 16, "Accent inference was not selectively restrained.");
			Assert(inferred.Actions.Where(item => item.Origin == EffectTreatmentOrigin.Automatic && (item.Type == EditorialUse.Flash || item.Type == EditorialUse.ScreenPump || item.Type == EditorialUse.Shake))
				.GroupBy(item => inferred.Actions.Count(prior => prior.TimeSeconds <= item.TimeSeconds && (prior.Type == EditorialUse.Flash || prior.Type == EditorialUse.ScreenPump || prior.Type == EditorialUse.Shake)))
				.All(group => group.Count() == 1), "Automatic visual treatments were duplicated.");
			Assert(inferred.Diagnostics.Any(item => item.Code == "region-density-limit" || item.Code == "spacing-limit" || item.Code == "repetition-limit"), "Effect restraint produced no explainable cooldown, density, or repetition diagnostic.");

			SongAnalysis beats = TreatmentAnalysis(MusicRegionType.Action, 16.0);
			for (int index = 0; index < 24; index++)
				beats.Events.Add(TreatmentEvent("beat-" + index, 0.5 + index * 0.5, MusicEventType.Beat));
			Assert(planner.Plan(beats).Actions.Count == 0, "Ordinary beats received automatic visual effects.");

			EffectTreatmentPlan first = planner.Plan(inference);
			EffectTreatmentPlan second = planner.Plan(inference);
			Assert(first.Actions.Select(item => item.EventId + "|" + item.Type + "|" + item.TimeSeconds.ToString("R") + "|" + item.Intensity.ToString("R") + "|" + item.DurationSeconds.ToString("R"))
				.SequenceEqual(second.Actions.Select(item => item.EventId + "|" + item.Type + "|" + item.TimeSeconds.ToString("R") + "|" + item.Intensity.ToString("R") + "|" + item.DurationSeconds.ToString("R"))),
				"Repeated automatic effect planning was not deterministic.");
		}

		private static void TestEffectSelectionOptions()
		{
			EffectSelectionOptions defaults = new EffectSelectionOptions();
			defaults.Validate();
			Assert(defaults.PresetId == EffectSelectionOptions.ConservativePresetId, "The Effects stage did not default to the conservative preset.");
			Assert(defaults.IncludeManualTreatments, "The Effects stage did not preserve manual song-map treatments by default.");
			Assert(defaults.Allows(EditorialUse.ScreenPump)
				&& defaults.Allows(EditorialUse.Flash)
				&& defaults.Allows(EditorialUse.Shake)
				&& defaults.Allows(EditorialUse.SpeedChange)
				&& defaults.Allows(EditorialUse.CutOrTransition)
				&& defaults.Allows(EditorialUse.CinematicTransition)
				&& defaults.Allows(EditorialUse.TitleReveal),
				"The default Effects-stage treatment families are incomplete.");
			Assert(!defaults.Allows(EditorialUse.GameplayAnchor), "A non-effect editorial role was exposed as an Effects-stage family.");

			EffectSelectionOptions screenPumpsOnly = new EffectSelectionOptions
			{
				EnableFlashes = false,
				EnableShake = false,
				EnableSpeedChanges = false,
				EnableTransitions = false,
				EnableTitles = false
			};
			Assert(screenPumpsOnly.Allows(EditorialUse.ScreenPump)
				&& !screenPumpsOnly.Allows(EditorialUse.Flash)
				&& !screenPumpsOnly.Allows(EditorialUse.Shake)
				&& !screenPumpsOnly.Allows(EditorialUse.SpeedChange)
				&& !screenPumpsOnly.Allows(EditorialUse.CutOrTransition)
				&& !screenPumpsOnly.Allows(EditorialUse.TitleReveal),
				"Effects-stage family switches did not map to editorial treatments.");

			AssertThrows<ArgumentOutOfRangeException>(() => new EffectSelectionOptions { Intensity = 2.01 }.Validate(), "Out-of-range effect intensity was accepted.");
			AssertThrows<ArgumentOutOfRangeException>(() => new EffectSelectionOptions { Density = -0.01 }.Validate(), "Out-of-range effect density was accepted.");
			AssertThrows<ArgumentException>(() => new EffectSelectionOptions { PresetId = "unknown" }.Validate(), "An unknown effect preset was accepted.");
			AssertThrows<NotSupportedException>(() => new EffectSelectionOptions { SchemaVersion = EffectSelectionOptions.CurrentSchemaVersion + 1 }.Validate(), "A future effect-selection schema was accepted.");

			AutomaticEffectTreatmentPlanner planner = new AutomaticEffectTreatmentPlanner();
			SongAnalysis analysis = TreatmentAnalysis(MusicRegionType.Action, 20.0);
			MusicEvent drop = TreatmentEvent("selected-drop", 3.0, MusicEventType.Drop);
			drop.Editorial.Assignments.Add(new EditorialAssignment { Use = EditorialUse.Flash, Origin = EditorialAssignmentOrigin.UserChosen });
			analysis.Events.Add(drop);

			EffectTreatmentPlan screenPumpPlan = planner.Plan(analysis, screenPumpsOnly.CreatePreset(), screenPumpsOnly);
			Assert(screenPumpPlan.Actions.Any(item => item.Type == EditorialUse.ScreenPump), "An enabled automatic treatment family was omitted.");
			Assert(screenPumpPlan.Actions.All(item => item.Type == EditorialUse.ScreenPump), "A disabled treatment family survived Effects-stage filtering.");

			EffectSelectionOptions manualOnly = new EffectSelectionOptions
			{
				PresetId = EffectSelectionOptions.NoAutomaticEffectsPresetId,
				EnableScreenPumps = false,
				EnableShake = false,
				EnableSpeedChanges = false,
				EnableTransitions = false,
				EnableTitles = false
			};
			EffectTreatmentPlan manualPlan = planner.Plan(analysis, manualOnly.CreatePreset(), manualOnly);
			Assert(manualPlan.Actions.Count == 1
				&& manualPlan.Actions[0].Type == EditorialUse.Flash
				&& manualPlan.Actions[0].Origin == EffectTreatmentOrigin.Manual,
				"The no-automation preset did not preserve an enabled manual song-map treatment.");

			manualOnly.IncludeManualTreatments = false;
			Assert(planner.Plan(analysis, manualOnly.CreatePreset(), manualOnly).Actions.Count == 0,
				"The no-automation preset produced a treatment after manual treatments were excluded.");
		}

		private static void TestPlacementAwareScreenPumps()
		{
			PlacementAwareEffectTreatmentPlanner planner = new PlacementAwareEffectTreatmentPlanner();
			SongAnalysis analysis = TreatmentAnalysis(MusicRegionType.Action, 12.0);
			analysis.Events.Add(TreatmentEvent("kill-a", 2.0, MusicEventType.Drop));
			analysis.Events.Add(TreatmentEvent("between-beat", 3.0, MusicEventType.Beat));
			analysis.Events.Add(TreatmentEvent("between-accent", 4.0, MusicEventType.Accent));
			analysis.Events.Add(TreatmentEvent("kill-b", 8.0, MusicEventType.Downbeat));
			List<ClipPlacement> placements = new List<ClipPlacement>
			{
				new ClipPlacement { TimelineStartSeconds = 1.0, LengthSeconds = 4.5 },
				new ClipPlacement { TimelineStartSeconds = 7.5, LengthSeconds = 1.5 }
			};
			List<MontageSyncAssignment> assignments = new List<MontageSyncAssignment>
			{
				new MontageSyncAssignment { ClipPath = "a.mp4", KillIndex = 0, MusicEventId = "kill-a", TimelineTimeSeconds = 2.0 },
				new MontageSyncAssignment { ClipPath = "b.mp4", KillIndex = 0, MusicEventId = "kill-b", TimelineTimeSeconds = 8.0 }
			};
			EffectSelectionOptions options = new EffectSelectionOptions();
			EffectTreatmentPlan existing = new EffectTreatmentPlan();
			existing.Actions.Add(new EffectTreatmentAction { EventId = "kill-a", TimeSeconds = 2.0, Type = EditorialUse.ScreenPump, Origin = EffectTreatmentOrigin.Automatic });
			EffectTreatmentPlan plan = planner.Plan(analysis, placements, assignments, options, existing);
			Assert(plan.Actions.Count(item => item.EventId == "kill-a" && item.Type == EditorialUse.ScreenPump) == 1, "A generic pump was not replaced by exactly one mandatory kill pump.");
			Assert(plan.Actions.Any(item => item.EventId == "kill-b" && item.Reason.Contains("mandatory")), "A reviewed kill did not receive a mandatory placement-aware pump.");
			Assert(plan.Actions.Count(item => item.EventId == "between-beat" || item.EventId == "between-accent") == 2, "Default density did not add the two-event conservative intermediate pocket.");
			EffectTreatmentPlan repeated = planner.Plan(analysis, placements, assignments, options, new EffectTreatmentPlan());
			Assert(plan.Actions.Select(item => item.EventId + "|" + item.TimeSeconds.ToString("R"))
				.SequenceEqual(repeated.Actions.Select(item => item.EventId + "|" + item.TimeSeconds.ToString("R"))),
				"Placement-aware screen-pump planning was not deterministic.");

			EffectSelectionOptions dense = new EffectSelectionOptions { Density = 1.5 };
			EffectTreatmentPlan densePlan = planner.Plan(analysis, placements, assignments, dense, new EffectTreatmentPlan());
			Assert(densePlan.Actions.Count(item => item.EventId == "between-beat" || item.EventId == "between-accent") == 2, "High density did not allow two eligible intermediate pumps.");
			MusicEvent intentionallyUnused = TreatmentEvent("ignored-beat", 4.5, MusicEventType.Beat);
			intentionallyUnused.Editorial.Assignments.Add(new EditorialAssignment { Use = EditorialUse.IntentionallyUnused, Origin = EditorialAssignmentOrigin.UserChosen });
			analysis.Events.Add(intentionallyUnused);
			EffectTreatmentPlan ignoredPlan = planner.Plan(analysis, placements, assignments, dense, new EffectTreatmentPlan());
			Assert(!ignoredPlan.Actions.Any(item => item.EventId == "ignored-beat"), "An intentionally-unused event received an intermediate pump.");
			analysis.Events.Add(TreatmentEvent("third-eligible", 5.0, MusicEventType.Downbeat));
			EffectTreatmentPlan longGapPlan = planner.Plan(analysis, placements, assignments, dense, new EffectTreatmentPlan());
			Assert(!longGapPlan.Actions.Any(item => item.EventId == "between-beat" || item.EventId == "between-accent" || item.EventId == "third-eligible"), "A gap with more than two eligible musical events received interstitial pumps.");
			EffectSelectionOptions disabled = new EffectSelectionOptions { EnableScreenPumps = false };
			Assert(planner.Plan(analysis, placements, assignments, disabled, new EffectTreatmentPlan()).Actions.Count == 0, "Disabled screen pumps still produced placement-aware actions.");

			assignments.Add(new MontageSyncAssignment { ClipPath = "missing.mp4", KillIndex = 0, MusicEventId = "missing-kill", TimelineTimeSeconds = 10.0 });
			EffectTreatmentPlan diagnosed = planner.Plan(analysis, placements, assignments, options, new EffectTreatmentPlan());
			Assert(diagnosed.Diagnostics.Any(item => item.Code == "kill-pump-outside-placement" && item.EventId == "missing-kill"), "An unrenderable mandatory kill pump was not diagnosed.");

		}

		private static SongAnalysis TreatmentAnalysis(MusicRegionType regionType, double duration)
		{
			return new SongAnalysis
			{
				Song = SyntheticIdentity("effect-treatment", duration),
				Regions = new List<MusicRegion> { TreatmentRegion("main-region", 0.0, duration, regionType) }
			};
		}

		private static MusicRegion TreatmentRegion(string id, double start, double end, MusicRegionType type)
		{
			return new MusicRegion { Id = id, StartSeconds = start, EndSeconds = end, Type = type, Origin = MusicAnalysisOrigin.Detected, ReviewState = MusicAnalysisReviewState.Proposed };
		}

		private static MusicEvent TreatmentEvent(string id, double time, MusicEventType type)
		{
			MusicEvent item = DetectedEvent(id, time);
			item.Type = type;
			item.DetectedType = type;
			item.Strength = 0.8;
			return item;
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

		private static void AssertThrows<TException>(Action action, string message) where TException : Exception
		{
			try
			{
				action();
			}
			catch (TException)
			{
				return;
			}

			throw new InvalidOperationException(message);
		}
	}
}
