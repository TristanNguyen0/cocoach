using CoCoach.Analysis;
using CoCoach.Analysis.Corners;
using CoCoach.Analysis.Recording;
using CoCoach.Analysis.Track;
using CoCoach.Tests.Support;

namespace CoCoach.Tests;

/// <summary>
/// The analysis engine on real iRacing recordings in <c>COCOACH_IBT_DIR</c> (default <c>samples/ibt/</c>).
/// Skipped when there are no recordings, or none with enough valid laps.
/// </summary>
public sealed class RealRecordingAnalysisTests
{
    /// <summary>
    /// Our delta must match iRacing's <c>LapDeltaToBestLap</c> to within this, seconds, on at least
    /// <see cref="DeltaAgreementShare"/> of the laps where iRacing's channel is self-consistent at the line.
    /// </summary>
    const double DeltaTolerance = 0.010;

    /// <summary>
    /// iRacing's channel can drift mid-lap and still agree at the line: on the Tsukuba sample, lap 4 against lap 3
    /// (the first best lap after a pit stop) wanders by up to 0.2 s while every other lap agrees within 2 ms.
    /// </summary>
    const double DeltaAgreementShare = 0.9;

    /// <summary>Recordings need this many valid complete laps to say anything about segmentation and references.</summary>
    const int MinValidLaps = 3;

    static readonly Lazy<Task<IReadOnlyList<(string File, AnalysisResult Result)>>> Analysed = new(LoadAsync);

    static async Task<IReadOnlyList<(string File, AnalysisResult Result)>> LoadAsync()
    {
        var results = new List<(string, AnalysisResult)>();
        foreach (var file in RecordingCorpus.Files())
        {
            var session = await SessionLoader.LoadIbtAsync(file);
            AnalysisResult result;
            try
            {
                result = SessionAnalyzer.Analyze([session]);
            }
            catch (InvalidOperationException)
            {
                continue;   // no valid complete lap (a hillclimb, a few seconds of pit lane)
            }
            if (result.Laps.Count(l => l.IsValid) >= MinValidLaps)
                results.Add((Path.GetFileName(file), result));
        }
        return results;
    }

    static async Task<IReadOnlyList<(string File, AnalysisResult Result)>> AnalysedOrSkip()
    {
        if (RecordingCorpus.Files().Length == 0)
            Assert.Skip($"No .ibt files in {RecordingCorpus.Directory}. Set {RecordingCorpus.EnvironmentVariable} or run scripts/fetch-sample-ibt.sh --large.");
        var analysed = await Analysed.Value;
        if (analysed.Count == 0)
            Assert.Skip($"No recording in {RecordingCorpus.Directory} has {MinValidLaps} valid complete laps (the --large samples do).");
        return analysed;
    }

    [Fact]
    public async Task Corner_counts_are_plausible()
    {
        foreach (var (file, r) in await AnalysedOrSkip())
        {
            Assert.True(r.CornerCount.Plausible, $"{file}: {r.CornerCount.Found} corners, iRacing lists {r.CornerCount.Expected}");
            Assert.InRange(r.CornerCount.Found, 4, 40);
            foreach (var c in r.Track.Corners)
                Assert.True(c.Start <= c.TurnIn && c.TurnIn <= c.Apex && c.Apex <= c.Exit && c.Exit <= c.End, $"{file} {c.Name}");
        }
    }

    [Fact]
    public async Task Segmentation_is_stable_across_laps_of_one_session()
    {
        // corners found from the odd clean laps and from the even ones must agree
        foreach (var (file, r) in await AnalysedOrSkip())
        {
            if (r.CleanLaps.Count < 4)
                continue;
            var odd = r.CleanLaps.Where((_, i) => i % 2 == 0).ToArray();
            var even = r.CleanLaps.Where((_, i) => i % 2 == 1).ToArray();
            var a = CornerDetector.Detect(TrackModelBuilder.Build(r.Key, odd), odd);
            var b = CornerDetector.Detect(TrackModelBuilder.Build(r.Key, even), even);

            // a dead-reckoned map from two or three laps is noisier than GPS: allow one more marginal split or kink
            var slack = r.Track.Source == TrackMapSource.Gps ? 1 : 2;
            Assert.True(Math.Abs(a.Count - b.Count) <= slack, $"{file}: {a.Count} vs {b.Count} corners");
            var matched = a.Count(c => b.Any(d => d.Direction == c.Direction && Math.Abs(d.Apex - c.Apex) <= 25));
            Assert.True(matched >= a.Count - slack, $"{file}: only {matched} of {a.Count} corners match between halves");

            // each clean lap brakes for the reference's braking corners close to where the reference does
            var clean = r.LapAnalyses.Where(l => r.CleanLaps.Contains(l.Trace)).ToArray();
            var rows = clean.SelectMany(l => l.Corners).Where(c => c.Reference.BrakePoint is not null).ToArray();
            var close = rows.Count(c => c.BrakePointOffset is { } o && Math.Abs(o) <= 40);
            Assert.True(close >= 0.8 * rows.Length, $"{file}: only {close} of {rows.Length} clean-lap brake points within 40 m of the reference");
        }
    }

    [Fact]
    public async Task Optimal_composite_is_never_slower_than_the_best_lap()
    {
        foreach (var (file, r) in await AnalysedOrSkip())
        {
            Assert.True(r.Optimal.LapTime <= r.Best.LapTime + 1e-9, $"{file}: optimal {r.Optimal.LapTime} vs best {r.Best.LapTime}");
            Assert.True(r.Optimal.LapTime > r.Best.LapTime * 0.97, $"{file}: optimal {r.Optimal.LapTime} is implausibly fast against {r.Best.LapTime}");
            Assert.Equal(r.Optimal.Segments.Sum(s => s.Time), r.Optimal.LapTime, 1e-9);
        }
    }

    [Fact]
    public async Task Optimal_composite_is_close_to_iRacings_own_optimal_lap()
    {
        // iRacing's optimal (read back from LapDeltaToOptimalLap) uses its own, coarser sectors: close, not equal
        var compared = 0;
        foreach (var (file, r) in await AnalysedOrSkip())
        {
            if (r.IracingOptimalLapTimes.FirstOrDefault(t => t is not null) is not { } iracing)
                continue;
            Assert.True(Math.Abs(r.Optimal.LapTime - iracing) <= 0.015 * r.Best.LapTime,
                $"{file}: our optimal {r.Optimal.LapTime:0.000} s, iRacing's {iracing:0.000} s");
            compared++;
        }
        if (compared == 0)
            Assert.Skip("No recording carries LapDeltaToOptimalLap.");
    }

    [Fact]
    public async Task Aligned_lap_times_match_iRacings()
    {
        foreach (var (file, r) in await AnalysedOrSkip())
        {
            // a race's first lap is timed by iRacing from the start, not the line (as in the Phase 0 replay test)
            foreach (var lap in r.Laps.Where(l => l is { IsValid: true, ReportedLapTime: not null }).Skip(1))
                Assert.True(Math.Abs(lap.LapTime - lap.ReportedLapTime!.Value) < 0.01, $"{file} {lap.Id}: {lap.LapTime:0.000} vs {lap.ReportedLapTime:0.000}");
        }
    }

    [Fact]
    public async Task Delta_agrees_with_iRacings_LapDeltaToBestLap()
    {
        // laps where iRacing's own delta at the line disagrees with its own lap times say nothing about ours
        var checks = (await AnalysedOrSkip())
            .SelectMany(a => a.Result.IracingDeltaChecks.Where(c => c.IracingSelfConsistent).Select(c => (a.File, Check: c)))
            .ToArray();
        if (checks.Length == 0)
            Assert.Skip("No recording has a self-consistent LapDeltaToBestLap against a lap inside the recording.");

        var outside = checks.Where(c => c.Check.MaxAbsError > DeltaTolerance).ToArray();
        var report = string.Join("; ", outside.Select(c =>
            $"{c.File} {c.Check.Lap} against {c.Check.Reference}: max |ours - iRacing| {c.Check.MaxAbsError:0.0000} s"));
        Assert.True(checks.Length - outside.Length >= DeltaAgreementShare * checks.Length,
            $"{outside.Length} of {checks.Length} laps outside {DeltaTolerance} s: {report}");
        var maxima = checks.Select(c => c.Check.MaxAbsError).Order().ToArray();
        Assert.True(maxima[maxima.Length / 2] <= DeltaTolerance, $"median lap's max error {maxima[maxima.Length / 2]:0.0000} s");
    }

    [Fact]
    public async Task Gps_maps_agree_with_yaw_rate_curvature()
    {
        // the sign convention (positive = left) holds for both curvature sources
        foreach (var (file, r) in await AnalysedOrSkip())
        {
            if (r.Track.Source != TrackMapSource.Gps)
                continue;
            var yaw = TrackModelBuilder.YawCurvature(r.CleanLaps[0]);
            if (yaw is null)
                continue;
            double dot = 0, a = 0, b = 0;
            for (var i = 0; i < yaw.Length; i++)
            {
                if (!double.IsFinite(yaw[i]))
                    continue;
                dot += yaw[i] * r.Track.Curvature[i];
                a += yaw[i] * yaw[i];
                b += r.Track.Curvature[i] * r.Track.Curvature[i];
            }
            Assert.True(dot / Math.Sqrt(a * b) > 0.9, $"{file}: correlation {dot / Math.Sqrt(a * b):0.000}");
        }
    }
}
