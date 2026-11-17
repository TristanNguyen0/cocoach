using CoCoach.Analysis;
using CoCoach.Analysis.Alignment;
using CoCoach.Analysis.Comparison;
using CoCoach.Analysis.Corners;
using CoCoach.Analysis.Recording;
using CoCoach.Analysis.References;
using CoCoach.Analysis.Track;
using CoCoach.Tests.Support;

namespace CoCoach.Tests;

/// <summary>
/// The analysis engine on a generated circuit whose corners, inputs and lap-time profiles are known exactly.
/// </summary>
public sealed class AnalysisTests
{
    static readonly SyntheticCircuit Circuit = new();

    // driven laps (0-based): two clean laps, one slow through corner 4, one that brakes later and harder for
    // corner 3 (the fastest), one a metre further inside in corner 3 that also uses the kerb in corner 4,
    // and one that runs 3 m wide and off the track in corner 7
    const int Clean = 0;
    const int Clean2 = 1;
    const int SlowC4 = 2;
    const int LateBrakerC3 = 3;
    const int InsideLineC3 = 4;
    const int OffTrackC7 = 5;

    static readonly LapStyle[] Styles =
    [
        LapStyle.Clean,
        LapStyle.Clean,
        new() { CornerSpeed = new Dictionary<int, double> { [4] = 0.9 } },
        new() { Decel = new Dictionary<int, double> { [3] = 13 } },
        new() { ApexOffset = new Dictionary<int, double> { [3] = 1.0 }, Kerbs = new HashSet<int> { 4 } },
        new() { OffTrackAt = 7, ApexOffset = new Dictionary<int, double> { [7] = -3.0 } },
    ];

    static readonly Lazy<SyntheticRun> Run = new(() => Circuit.Session(Styles));
    static readonly Lazy<AnalysisResult> Result = new(() => Analyze(Run.Value));

    static AnalysisResult Analyze(SyntheticRun run, AnalysisOptions? options = null, string label = "synthetic.ibt") =>
        SessionAnalyzer.Analyze([SessionLoader.FromFrames(label, run.Frames, Circuit.Facts())], options);

    static LapTrace Trace(int driven) => Result.Value.Laps.Single(l => l.Id.Lap == SyntheticRun.LapNumber(driven));

    static LapAnalysis Lap(int driven) => Result.Value.LapAnalyses.Single(a => a.Trace.Id.Lap == SyntheticRun.LapNumber(driven));

    static Corner CornerFor(CircuitArc arc) =>
        Result.Value.Track.Corners.Single(c => c.Apex >= arc.Start - 5 && c.Apex <= arc.End + 5);

    [Fact]
    public void Laps_resample_onto_a_one_metre_lapdist_grid()
    {
        var length = Circuit.Length;
        Assert.Equal(Styles.Length, Result.Value.Laps.Count);
        Assert.Equal(length, Result.Value.Track.Length, 0.01);

        for (var k = 0; k < Styles.Length; k++)
        {
            var trace = Trace(k);
            var truth = Run.Value.Laps[k];
            Assert.Equal(1.0, trace.Step);
            Assert.Equal((int)Math.Floor(length) + 1, trace.Count);
            Assert.Equal(0, trace.Time[0], 1e-9);
            Assert.Equal(truth.LapTime, trace.LapTime, 0.001);
            for (var i = 1; i < trace.Count; i++)
                Assert.True(trace.Time[i] > trace.Time[i - 1]);
            for (var i = 0; i < trace.Count; i += 7)
            {
                Assert.Equal(truth.TimeAt(i), trace.Time[i], 0.001);
                Assert.Equal(truth.SpeedAt(i), trace.Speed[i], 0.1);
            }
        }
    }

    [Fact]
    public void Delta_trace_is_the_distance_aligned_time_difference()
    {
        var lap = Trace(SlowC4);
        var reference = Trace(Clean);
        var delta = DeltaTrace.Compute(lap, reference);
        var slow = Run.Value.Laps[SlowC4];
        var clean = Run.Value.Laps[Clean];

        for (var i = 0; i < delta.Length; i += 5)
            Assert.Equal(slow.TimeAt(i) - clean.TimeAt(i), delta[i], 0.002);
        Assert.Equal(slow.LapTime - clean.LapTime, lap.LapTime - reference.LapTime, 0.001);

        // identical driving gives a flat zero delta
        Assert.All(DeltaTrace.Compute(Trace(Clean2), reference), d => Assert.Equal(0, d, 0.001));
    }

    [Fact]
    public void Corners_are_found_where_the_circuit_turns()
    {
        var corners = Result.Value.Track.Corners;
        Assert.Equal(Circuit.Arcs.Count, corners.Count);
        Assert.True(Result.Value.CornerCount.Plausible);
        Assert.Equal(Circuit.Arcs.Count, Result.Value.CornerCount.Expected);

        for (var c = 0; c < corners.Count; c++)
        {
            var arc = Circuit.Arcs[c];
            var corner = corners[c];
            Assert.Equal(c + 1, corner.Number);
            Assert.Equal(arc.Sign > 0 ? CornerDirection.Left : CornerDirection.Right, corner.Direction);
            Assert.InRange(corner.TurnIn, arc.Start - 20, arc.Start + 20);
            Assert.InRange(corner.Exit, arc.End - 20, arc.End + 20);
            Assert.InRange(corner.Apex, arc.Start, arc.End);
            // smoothing spreads curvature across the change of direction in the esses
            Assert.InRange(corner.HeadingChange, arc.Degrees * 0.8, arc.Degrees * 1.1);
            Assert.InRange(corner.MinRadius, arc.Radius * 0.9, arc.Radius * 1.5);
            Assert.True(corner.Start <= corner.TurnIn && corner.TurnIn < corner.Apex && corner.Apex < corner.Exit && corner.Exit <= corner.End);
        }

        // the esses split at the change of direction, with no gap between the two halves
        Assert.Equal(CornerDirection.Left, corners[0].Direction);
        Assert.Equal(CornerDirection.Right, corners[1].Direction);
        Assert.Equal(corners[0].End, corners[1].Start);

        // windows never overlap and the segments tile the lap
        for (var c = 0; c + 1 < corners.Count; c++)
        {
            Assert.True(corners[c].End <= corners[c + 1].Start);
            Assert.Equal(corners[c].SegmentEnd, corners[c + 1].SegmentStart);
        }
        Assert.Equal(0, corners[0].SegmentStart);
        Assert.Equal(Circuit.Length, corners[^1].SegmentEnd, 0.01);
    }

    [Fact]
    public void Braking_corners_start_their_window_at_the_typical_brake_point()
    {
        foreach (var arc in Circuit.Arcs)
        {
            var corner = CornerFor(arc);
            if (Run.Value.Laps[Clean].BrakeStart[arc.Number] is { } brake)
                Assert.InRange(corner.Start, brake - 15, brake - 5);
        }
    }

    [Fact]
    public void Key_points_match_the_generated_inputs()
    {
        for (var k = 0; k < Styles.Length; k++)
        {
            var truth = Run.Value.Laps[k];
            foreach (var c in Lap(k).Corners)
            {
                var arc = Circuit.Arcs[c.Corner.Number - 1];
                var points = c.Lap;
                if (truth.BrakeStart[arc.Number] is { } brake)
                {
                    Assert.NotNull(points.BrakePoint);
                    Assert.Equal(brake, points.BrakePoint.Value, 2.0);
                    Assert.Equal(0.8, points.PeakBrake!.Value, 0.01);
                    Assert.InRange(points.BrakeRelease!.Value, arc.Start - 3, arc.Start + 3);
                    Assert.NotNull(points.ThrottlePickup);
                    Assert.InRange(points.ThrottlePickup.Value, arc.Start - 3, arc.Start + 3);
                }
                else
                {
                    Assert.Null(points.BrakePoint);
                }
                Assert.Equal(truth.MinSpeed[arc.Number], points.MinSpeed, 0.1);
                Assert.InRange(points.ApexPoint, arc.Start - 3, arc.End + 3);
                Assert.NotNull(points.TurnInPoint);
                Assert.InRange(points.TurnInPoint.Value, arc.Start - 20, arc.Start + 15);
                Assert.Equal(truth.TimeAt(c.Corner.End) - truth.TimeAt(c.Corner.Start), points.Time, 0.002);
            }
        }
    }

    [Fact]
    public void The_best_valid_lap_is_the_reference()
    {
        var r = Result.Value;
        Assert.Equal(SyntheticRun.LapNumber(LateBrakerC3), r.Best.Trace.Id.Lap);
        Assert.Same(r.Best, r.Reference);
        Assert.False(Trace(OffTrackC7).IsValid);
        Assert.True(Trace(OffTrackC7).OffTrack);
        Assert.DoesNotContain(r.Optimal.Segments, s => s.Lap.Lap == SyntheticRun.LapNumber(OffTrackC7));
    }

    [Fact]
    public void Metrics_against_the_reference_show_the_known_differences()
    {
        var truth = Run.Value.Laps;
        var c3 = Circuit.Arcs[2];
        var c4 = Circuit.Arcs[3];

        // slow through corner 4: lower minimum speed, earlier braking, time lost there and nowhere else
        var slow = Lap(SlowC4).Corners.Single(c => c.Corner.Number == 4);
        Assert.Equal(truth[SlowC4].MinSpeed[4] - truth[LateBrakerC3].MinSpeed[4], slow.MinSpeedDifference, 0.1);
        Assert.True(slow.MinSpeedDifference < -1.5);
        Assert.Equal(truth[LateBrakerC3].BrakeStart[4]!.Value - truth[SlowC4].BrakeStart[4]!.Value, slow.BrakePointOffset!.Value, 2.0);
        Assert.True(slow.BrakePointOffset > 1);   // 10 % slower at 10 m/s² is about 2 m earlier
        Assert.True(slow.TimeDelta > 0.1);
        Assert.True(slow.MidDelta > 0.05);

        // a clean lap against the late braker: brakes earlier for corner 3 and loses time on the entry
        var clean = Lap(Clean).Corners.Single(c => c.Corner.Number == 3);
        var expectedOffset = truth[LateBrakerC3].BrakeStart[3]!.Value - truth[Clean].BrakeStart[3]!.Value;
        Assert.True(expectedOffset > 10);
        Assert.Equal(expectedOffset, clean.BrakePointOffset!.Value, 2.0);
        Assert.True(clean.EntryDelta > 0.02);
        Assert.Equal(0, clean.MinSpeedDifference, 0.1);
        Assert.Equal(0, clean.ThrottlePickupOffset!.Value, 2.0);

        // everywhere the laps were driven the same, the deltas are zero
        foreach (var c in Lap(Clean).Corners.Where(c => c.Corner.Number is not 3))
            Assert.Equal(0, c.TimeDelta, 0.003);
        Assert.Equal(c3.Number, CornerFor(c3).Number);
        Assert.Equal(c4.Number, CornerFor(c4).Number);
    }

    [Fact]
    public void Line_offset_and_kerb_usage_are_measured_against_the_gps_map()
    {
        var r = Result.Value;
        Assert.Equal(TrackMapSource.Gps, r.Track.Source);

        // the generated offset fades in and out across the arc; compare at the detected apex
        var inside = Lap(InsideLineC3).Corners.Single(c => c.Corner.Number == 3);
        var expected = Circuit.OffsetAt(Styles[InsideLineC3], inside.Corner.Apex);
        Assert.True(expected > 0.5);
        Assert.Equal(expected, inside.ApexLineOffset!.Value, 0.1);
        Assert.Equal(0, Lap(Clean).Corners.Single(c => c.Corner.Number == 3).ApexLineOffset!.Value, 0.05);

        var kerb = Lap(InsideLineC3).Corners.Single(c => c.Corner.Number == 4);
        Assert.Equal(20, kerb.Lap.KerbMetres!.Value, 2.0);
        Assert.Equal(0, Lap(Clean).Corners.Single(c => c.Corner.Number == 4).Lap.KerbMetres);

        // edges: on the main straight every lap is on the centreline, so the edges sit half a car either side;
        // in corner 7 one lap ran up to 3 m wide, which widens the right-hand edge where it was still on track,
        // but never past where it was off track
        var left = r.Track.LeftEdge!;
        var right = r.Track.RightEdge!;
        for (var i = 0; i < 150; i++)
        {
            Assert.Equal(TrackModelBuilder.HalfCarWidth, left[i], 0.2);
            Assert.Equal(-TrackModelBuilder.HalfCarWidth, right[i], 0.2);
        }
        var c7 = (int)Circuit.Arcs[6].Mid;
        Assert.True(right[c7 - 12] < -2.5, $"right edge before the excursion: {right[c7 - 12]}");
        Assert.InRange(right[c7], -3.1, -0.5);
    }

    [Fact]
    public void Pit_lane_positions_do_not_widen_the_track_edges()
    {
        // the third lap starts from a pit lane 20 m right of the main straight, which heads east
        var run = Circuit.Session([LapStyle.Clean, LapStyle.Clean, LapStyle.Clean]);
        var pitLap = SyntheticRun.LapNumber(2);
        var frames = run.Frames.Select(f =>
        {
            if (f.Lap != pitLap || f.LapDist >= 150 || f.Lat is not { } lat || f.Lon is not { } lon)
                return f;
            var (east, north, up) = Geodesy.ToEnu(SyntheticCircuit.Origin, lat, lon, f.Alt ?? 0);
            var (pitLat, pitLon, _) = Geodesy.FromEnu(SyntheticCircuit.Origin, east, north - 20, up);
            return f with { Lat = pitLat, Lon = pitLon, OnPitRoad = true, PlayerTrackSurface = 2 };
        });
        var r = SessionAnalyzer.Analyze([SessionLoader.FromFrames("pit.ibt", frames, Circuit.Facts())]);

        Assert.True(r.Laps.Single(l => l.Id.Lap == pitLap).PitRoad);
        for (var i = 0; i < 150; i++)
        {
            Assert.Equal(TrackModelBuilder.HalfCarWidth, r.Track.LeftEdge![i], 0.2);
            Assert.Equal(-TrackModelBuilder.HalfCarWidth, r.Track.RightEdge![i], 0.2);
        }
    }

    [Fact]
    public void A_lapdistpct_just_below_zero_after_the_line_keeps_the_lap_and_its_tracker_time()
    {
        var run = Circuit.Session([LapStyle.Clean, LapStyle.Clean, LapStyle.Clean]);
        var frames = run.Frames.ToArray();
        for (var k = 1; k < frames.Length; k++)
        {
            if (frames[k].Lap != frames[k - 1].Lap)
                frames[k] = frames[k] with { LapDistPct = -0.0002f, LapDist = (float)(-0.0002 * Circuit.Length) };
        }
        var session = SessionLoader.FromFrames("negative.ibt", frames, Circuit.Facts());
        var r = SessionAnalyzer.Analyze([session]);

        var complete = session.Laps.Where(l => l.IsComplete).ToArray();
        Assert.Equal(3, complete.Length);
        Assert.Equal(complete.Select(l => l.Lap), r.Laps.Select(l => l.Id.Lap));
        foreach (var lap in complete)
            Assert.Equal(lap.LapTime!.Value, r.Laps.Single(l => l.Id.Lap == lap.Lap).LapTime, 1e-6);
    }

    [Fact]
    public void Optimal_composite_takes_the_best_segment_from_each_lap()
    {
        // one lap fast in corner 3, another fast in corner 4: the composite beats both
        var styles = new LapStyle[]
        {
            new() { Decel = new Dictionary<int, double> { [3] = 13 } },
            new() { Decel = new Dictionary<int, double> { [4] = 13 } },
            LapStyle.Clean,
        };
        var run = Circuit.Session(styles);
        var r = Analyze(run, new AnalysisOptions { Reference = ReferenceKind.Optimal });

        Assert.Same(r.Optimal, r.Reference);
        Assert.True(r.Optimal.LapTime < r.Best.LapTime - 0.01);
        Assert.Equal(r.Optimal.Segments.Sum(s => s.Time), r.Optimal.LapTime, 1e-9);
        foreach (var segment in r.Optimal.Segments)
        {
            var times = r.Laps.Select(l => l.TimeBetween(segment.From, segment.To));
            Assert.Equal(times.Min(), segment.Time, 1e-9);
        }
        var composite = r.Optimal.Trace;
        Assert.Equal(0, composite.Time[0]);
        for (var i = 1; i < composite.Count; i++)
            Assert.True(composite.Time[i] > composite.Time[i - 1]);
        Assert.Equal(2, composite.ComposedFrom.Count(id => id.Lap != SyntheticRun.LapNumber(2)));

        // expected: lap 0's corner-3 gain plus lap 1's corner-4 gain over the clean lap
        var clean = run.Laps[2];
        var gain3 = clean.LapTime - run.Laps[0].LapTime;
        var gain4 = clean.LapTime - run.Laps[1].LapTime;
        Assert.Equal(clean.LapTime - gain3 - gain4, r.Optimal.LapTime, 0.003);
    }

    [Fact]
    public void Synthetic_iracing_delta_agrees_with_ours()
    {
        var checks = Result.Value.IracingDeltaChecks;
        Assert.Equal(Styles.Length - 1, checks.Count);
        foreach (var check in checks)
        {
            Assert.True(check.IracingSelfConsistent);
            Assert.True(check.MaxAbsError < 0.002, $"{check.Lap}: {check.MaxAbsError}");
        }
    }

    [Fact]
    public void Without_gps_the_map_is_dead_reckoned_with_loop_closure_and_finds_the_same_corners()
    {
        var run = Circuit.Session(Styles, gps: false);
        var r = Analyze(run);

        Assert.Equal(TrackMapSource.DeadReckoning, r.Track.Source);
        Assert.NotNull(r.Track.ClosureError);
        Assert.True(r.Track.ClosureError < 10, $"closure error {r.Track.ClosureError} m");
        Assert.Null(r.Track.LeftEdge);
        Assert.Equal(Result.Value.Track.Corners.Count, r.Track.Corners.Count);
        foreach (var (gps, dr) in Result.Value.Track.Corners.Zip(r.Track.Corners))
        {
            Assert.Equal(gps.Direction, dr.Direction);
            Assert.Equal(gps.TurnIn, dr.TurnIn, 5.0);
            Assert.Equal(gps.Exit, dr.Exit, 5.0);
        }

        // the dead-reckoned map closes: the last point is next to the first
        var m = r.Track;
        var gap = Math.Sqrt(Math.Pow(m.X[^1] - m.X[0], 2) + Math.Pow(m.Y[^1] - m.Y[0], 2));
        Assert.True(gap < 2, $"gap {gap} m");
        Assert.All(r.LapAnalyses.SelectMany(a => a.Corners), c => Assert.Null(c.ApexLineOffset));
    }

    [Fact]
    public void Several_sessions_share_one_reference()
    {
        var first = Circuit.Session([LapStyle.Clean, new() { CornerSpeed = new Dictionary<int, double> { [4] = 0.9 } }]);
        var second = Circuit.Session([new() { Decel = new Dictionary<int, double> { [3] = 13 } }], startTime: 900);
        var r = SessionAnalyzer.Analyze(
        [
            SessionLoader.FromFrames("monday.ibt", first.Frames, Circuit.Facts()),
            SessionLoader.FromFrames("tuesday.ibt", second.Frames, Circuit.Facts()),
        ]);

        Assert.True(r.MultipleSessions);
        Assert.Equal(3, r.Laps.Count);
        Assert.Equal((1, "tuesday.ibt", 2), (r.Best.Trace.Id.Session, r.Best.Trace.Id.SessionLabel, r.Best.Trace.Id.Lap));
        Assert.Equal("S2 L2", r.Best.Trace.Id.Display(multipleSessions: true));
        Assert.True(r.Optimal.LapTime <= r.Best.LapTime + 1e-9);
    }

    [Fact]
    public void Laps_whose_number_repeats_after_a_reset_stay_distinct()
    {
        var before = Circuit.Session([LapStyle.Clean, new() { CornerSpeed = new Dictionary<int, double> { [4] = 0.9 } }]);
        var after = Circuit.Session([new() { Decel = new Dictionary<int, double> { [3] = 13 } }, LapStyle.Clean], startTime: 900);
        var frames = before.FollowedByReset(after);

        var r = SessionAnalyzer.Analyze([SessionLoader.FromFrames("reset.ibt", frames, Circuit.Facts())], new AnalysisOptions { Reference = ReferenceKind.Best });

        Assert.Equal(4, r.Laps.Count);
        Assert.Equal(2, r.Laps.Count(l => l.Id.Lap == SyntheticRun.LapNumber(0)));
        Assert.Equal(4, r.Laps.Select(l => l.Id).Distinct().Count());
        Assert.Equal(SyntheticRun.LapNumber(0), r.Best.Trace.Id.Lap);
        Assert.Equal(2, r.Best.Trace.Id.Ordinal);
        Assert.Equal(["L2", "L3", "L2#2", "L3#2"], r.Laps.Select(l => l.Id.Display(multipleSessions: false)));
        Assert.Equal("S1 L2#2", r.Best.Trace.Id.Display(multipleSessions: true));
        Assert.Single(r.LapAnalyses, a => a.Trace.Id == r.Reference.Trace.Id);
        Assert.All(r.CornerLosses, l => Assert.Equal(3, l.Laps));
        Assert.True(r.Optimal.LapTime <= r.Best.LapTime + 1e-9);
    }

    [Fact]
    public void Sessions_of_different_layouts_or_cars_are_refused()
    {
        var run = Circuit.Session([LapStyle.Clean]);
        var a = SessionLoader.FromFrames("a.ibt", run.Frames, Circuit.Facts());
        var otherConfig = SessionLoader.FromFrames("b.ibt", run.Frames, Circuit.Facts(config: "Short"));
        var otherCar = SessionLoader.FromFrames("c.ibt", run.Frames, Circuit.Facts() with { PlayerCarPath = "othercar" });

        Assert.Throws<InvalidOperationException>(() => SessionAnalyzer.Analyze([a, otherConfig]));
        Assert.Throws<InvalidOperationException>(() => SessionAnalyzer.Analyze([a, otherCar]));
    }

    [Fact]
    public void Analysis_is_deterministic()
    {
        var again = Analyze(Run.Value);
        var r = Result.Value;
        Assert.Equal(r.Track.Corners, again.Track.Corners);
        Assert.Equal(r.Optimal.LapTime, again.Optimal.LapTime);
        Assert.Equal(
            r.LapAnalyses.SelectMany(a => a.Corners).Select(c => (c.TimeDelta, c.BrakePointOffset, c.MinSpeedDifference)),
            again.LapAnalyses.SelectMany(a => a.Corners).Select(c => (c.TimeDelta, c.BrakePointOffset, c.MinSpeedDifference)));
    }

    [Fact]
    public void Top_time_loss_corners_rank_the_slow_corner_first()
    {
        // against the late braker every other lap loses in corner 3; the slow lap also loses in corner 4
        var losses = Result.Value.CornerLosses;
        Assert.Equal(3, losses[0].Corner.Number);
        Assert.True(losses[0].TimeLost > 0.02);
        Assert.True(losses[0].EntryLost > losses[0].ExitLost);
    }

    [Fact]
    public void Track_model_round_trips_through_the_file_store()
    {
        using var dir = new TempDir();
        var store = new JsonFileTrackModelStore(dir.Path);
        var model = Result.Value.Track;

        Assert.Null(store.Load(model.Key));
        store.Save(model);
        var loaded = store.Load(model.Key);

        Assert.NotNull(loaded);
        Assert.Equal(model.Key, loaded.Key);
        Assert.Equal(model.Source, loaded.Source);
        Assert.Equal(model.Length, loaded.Length);
        Assert.Equal(model.X, loaded.X);
        Assert.Equal(model.Curvature, loaded.Curvature);
        Assert.Equal(model.LeftEdge, loaded.LeftEdge);
        Assert.Equal(model.Corners, loaded.Corners);
        Assert.Equal(model.Origin, loaded.Origin);
        Assert.EndsWith("track-999-test.json", store.PathFor(model.Key), StringComparison.Ordinal);

        // another layout of the same track is a different file
        Assert.Null(store.Load(model.Key with { Config = "Short" }));
    }

    [Fact]
    public void Enu_conversion_round_trips()
    {
        var origin = SyntheticCircuit.Origin;
        var (lat, lon, alt) = Geodesy.FromEnu(origin, 1234.5, -678.9, 12.3);
        var (e, n, u) = Geodesy.ToEnu(origin, lat, lon, alt);
        Assert.Equal(1234.5, e, 1e-4);
        Assert.Equal(-678.9, n, 1e-4);
        Assert.Equal(12.3, u, 1e-4);
    }

    [Fact]
    public void Corner_count_check_tolerates_a_few_kinks()
    {
        Assert.True(CornerCountCheck.For(8, 12).Plausible);
        Assert.False(CornerCountCheck.For(4, 12).Plausible);
        Assert.True(CornerCountCheck.For(2, 4).Plausible);
        Assert.True(CornerCountCheck.For(7, null).Plausible);
    }
}
