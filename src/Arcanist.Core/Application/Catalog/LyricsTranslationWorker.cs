using Arcanist.Core.Data;

namespace Arcanist.Core.Application.Catalog;

/// <summary>
/// Makes the translations of lyrics players ask for, one at a time and at low priority, with the translator of the
/// extension that provides one; and once a day forgets the readings and translations of lyrics no track has any more.
/// </summary>
internal sealed class LyricsTranslationWorker(LyricsLanguages languages, DatabaseMaintenance maintenance, ILogger<LyricsTranslationWorker> logger)
    : BackgroundService
{
    /// <summary>The longest a song's translation may take (a slow model on a small server).</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(20);

    private static readonly TimeSpan PurgeEvery = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var nextPurge = DateTime.UtcNow.AddMinutes(10);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (languages.Take() is { } job)
                {
                    await TranslateAsync(job, stoppingToken);
                    continue;
                }

                if (DateTime.UtcNow >= nextPurge)
                {
                    nextPurge = DateTime.UtcNow + PurgeEvery;
                    using (await maintenance.WorkAsync(stoppingToken))
                    {
                        var purged = await languages.PurgeAsync(stoppingToken);
                        if (purged > 0)
                            logger.LogInformation("Forgot {Count} readings and translations of lyrics no track has", purged);
                    }
                }

                await languages.WaitAsync(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Translating lyrics failed");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task TranslateAsync(LyricsLanguages.Job job, CancellationToken stoppingToken)
    {
        IReadOnlyList<string>? lines = null;
        string? engine = null;
        if (languages.Translator is { } translator)
        {
            engine = translator.Engine;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            limit.CancelAfter(Limit);
            try
            {
                // Work out of the request threads' way: translating is long and heavy.
                var translated = await Task.Factory.StartNew(() => translator.TranslateAsync(job.Lines, job.From, job.Language, limit.Token), limit.Token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
                // As many lines as the lyrics, or the times would go with the wrong lines.
                if (translated.Count == job.Lines.Count)
                    lines = translated.Select((line, i) => string.IsNullOrWhiteSpace(job.Lines[i]) ? "" : line.Trim()).ToList();
                else
                    logger.LogWarning("{Engine} translated {Count} lines of {Expected} into {Language}: not kept", engine, translated.Count, job.Lines.Count, job.Language);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Translating lyrics into {Language} took longer than {Limit}", job.Language, Limit);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Translating lyrics into {Language} with {Engine} failed", job.Language, engine);
            }
        }

        using (await maintenance.WorkAsync(stoppingToken))
            await languages.FinishAsync(job, lines, engine, stoppingToken);
    }
}
