using Arcanist.Core.Analysis;
using Arcanist.Core.Application.Analysis;
using Arcanist.Core.Data;
using Arcanist.Core.Data.Entities;
using Arcanist.Core.Setup;
using Arcanist.Core.Tests.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcanist.Core.Tests.Analysis;

public sealed class AnalysisWorkerTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly ArcanistFactory _factory = new(new Dictionary<string, string?> { ["Arcanist:Analysis:Enabled"] = "true" });
    private readonly string _music = Directory.CreateTempSubdirectory("arcanist-analysis-").FullName;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Analyzes_each_distinct_audio_once_and_measures_the_album()
    {
        var album = new FixtureTags(Album: "Pulse", AlbumArtist: "Metronome", Artist: "Metronome");
        AudioFixtures.WriteMusic(Path.Combine(_music, "Pulse", "01.wav"), album with { Title = "Slow", TrackNumber = 1 }, bpm: 96, chordSeed: 2);
        AudioFixtures.WriteMusic(Path.Combine(_music, "Pulse", "02.wav"), album with { Title = "Fast", TrackNumber = 2 }, bpm: 140, chordSeed: 7);
        // The same audio again in another folder: one more file, no more analysis.
        Directory.CreateDirectory(Path.Combine(_music, "Copies"));
        File.Copy(Path.Combine(_music, "Pulse", "01.wav"), Path.Combine(_music, "Copies", "01.wav"));

        await _factory.CompleteSetupAsync(new SetupLibrary("Music", [_music]));
        await _factory.WaitForScansAsync(await _factory.CreateAuthenticatedClientAsync());
        var progress = await _factory.WaitForAnalysisAsync();

        Assert.Equal(new { Pending = 0, Analyzed = 2, Failed = 0 }, new { progress.Pending, progress.Analyzed, progress.Failed });

        await using var db = _factory.Services.GetRequiredService<IArcanistDbContextFactory>().CreateDbContext();
        var analyses = await db.AudioAnalyses.OrderBy(a => a.Bpm).ToListAsync(Cancel);
        Assert.Equal(2, analyses.Count);
        Assert.All(analyses, a =>
        {
            Assert.Equal(AnalysisState.Analyzed, a.State);
            Assert.Equal(AudioAnalyzer.Version, a.AnalyzerVersion);
            Assert.Equal(15_000, a.DurationMs);
            Assert.Equal(1000, a.Waveform!.Length);
            Assert.NotNull(a.AcoustIdFingerprint);
            Assert.NotNull(a.KeyTonic);
        });
        Assert.InRange(analyses[0].Bpm!.Value, 95, 97);
        Assert.InRange(analyses[1].Bpm!.Value, 139, 141);

        var measured = await db.Albums.SingleAsync(a => a.Title == "Pulse", Cancel);
        Assert.NotNull(measured.Loudness);
        Assert.InRange(measured.Loudness!.Value, analyses.Min(a => a.Loudness!.Value) - 0.1, analyses.Max(a => a.Loudness!.Value) + 0.1);
        Assert.Equal(analyses.Max(a => a.TruePeak), measured.TruePeak);
    }

    [Fact]
    public async Task Can_be_turned_off_from_the_settings()
    {
        AudioFixtures.WriteMusic(Path.Combine(_music, "01.wav"), new FixtureTags(Title: "Only"));
        await _factory.CompleteSetupAsync();
        await _factory.Services.GetRequiredService<SettingsStore>().SetAsync(SettingKeys.AnalysisEnabled, false, Cancel);

        var worker = _factory.Services.GetRequiredService<AnalysisWorker>();
        await worker.RunOnceAsync(Cancel);

        var progress = _factory.Services.GetRequiredService<AnalysisMonitor>().Current;
        Assert.Equal(AnalysisActivity.Disabled, progress.Activity);
        Assert.False(progress.Enabled);
    }

    [Fact]
    public void Album_loudness_of_a_single_track_is_its_own_loudness()
    {
        using var stream = new MemoryStream(AudioFixtures.Music(bpm: 120, seconds: 30));
        var loudness = AudioAnalyzer.Analyze(stream, "wav", AnalysisParts.Loudness).Analysis!.Loudness!;

        Assert.Equal(loudness.Integrated, AlbumLoudness.Integrate([loudness.Histogram])!.Value, 1);
        Assert.Null(AlbumLoudness.Integrate([]));
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_music, recursive: true);
    }
}
