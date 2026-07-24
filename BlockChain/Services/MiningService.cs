using BlockChain.Components;
using BlockChain.Models;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BlockChain.Services;

public readonly record struct MiningProgressSnapshot(
    long Attempts,
    long CurrentNonce,
    double HashRate,
    char MeasureUnit,
    TimeSpan Elapsed,
    string FormattedElapsed);

public class MiningService
{
    private const long RangeSize = 50_000;
    private const long AttemptBatchSize = 2048;
    private static readonly char[] MeasureUnits = [' ', 'k', 'M', 'G', 'T', 'P', 'E'];
    private readonly HashingService _hashingService;

    public MiningService(HashingService hashingService)
    {
        _hashingService = hashingService;
    }

    public (double hashRate, char measureUnit, string timeTaken, long nonce) MineBlock(
        Block block,
        int difficulty,
        bool showProgress = true)
    {
        return MineBlockAsync(block, difficulty, showProgress).GetAwaiter().GetResult();
    }

    public async Task<(double hashRate, char measureUnit, string timeTaken, long nonce)> MineBlockAsync(
        Block block,
        int difficulty,
        bool showProgress = true,
        Action<MiningProgressSnapshot>? reportProgress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateMiningArguments(block, difficulty);

        MiningResult result;
        if (showProgress)
        {
            result = await MineWithProgressAsync(
                block,
                difficulty,
                reportProgress,
                cancellationToken);
            WriteMiningResult(result);
        }
        else
        {
            result = await MineCoreAsync(
                block,
                difficulty,
                reportProgress,
                cancellationToken);
        }

        return (result.HashRate, MeasureUnits[result.MeasureUnitIndex], result.TimeTaken, result.Nonce);
    }

    private async Task<MiningResult> MineWithProgressAsync(
        Block block,
        int difficulty,
        Action<MiningProgressSnapshot>? externalProgress,
        CancellationToken cancellationToken)
    {
        MiningResult result = default;

        var progress = AnsiConsole.Progress()
            .Columns(
                new SpinnerColumn(new MinerSpinner())
                {
                    Style = new Style(Color.Orange1)
                },
                new TaskDescriptionColumn
                {
                    Wrap = true,
                    Alignment = Justify.Left
                })
            .AutoRefresh(false)
            .AutoClear(true);

        await progress.StartAsync(async context =>
        {
            var miningTask = context.AddTask(
                $"[bold orange1]Mining block #{block.Index}[/]",
                maxValue: 100);
            miningTask.IsIndeterminate = true;

            var statisticsTask = context.AddTask(
                "[bold cyan]Statistics:[/] [dim]waiting for samples...[/]",
                maxValue: 0);
            statisticsTask.IsIndeterminate = true;

            result = await MineCoreAsync(
                block,
                difficulty,
                snapshot =>
                {
                    statisticsTask.Description =
                        $"[bold cyan]Statistics:[/] [green]{snapshot.HashRate:F2} {snapshot.MeasureUnit}H/s[/] | " +
                        $"[white]{snapshot.Attempts:N0} attempts[/] | [cyan]{snapshot.FormattedElapsed}[/]";

                    externalProgress?.Invoke(snapshot);
                    context.Refresh();
                },
                cancellationToken);

            miningTask.IsIndeterminate = false;
            miningTask.Value = 100;
            miningTask.Description = $"[bold green]Completed block #{block.Index}[/]";

            statisticsTask.IsIndeterminate = false;
            statisticsTask.Value = 100;
            context.Refresh();
        });

        return result;
    }

    private async Task<MiningResult> MineCoreAsync(
        Block block,
        int difficulty,
        Action<MiningProgressSnapshot>? reportProgress,
        CancellationToken cancellationToken)
    {
        block.Difficulty = difficulty;

        string targetPrefix = new('0', difficulty);
        string transactionsRow = string.Concat(block.Transactions.Select(t => t.ToRowString()));
        string blockPrefix = $"{block.Index}{block.TimeStamp:o}{transactionsRow}{block.PrevHash}{block.Difficulty}";

        long attempts = 0;
        long currentNonce = 0;
        long winningNonce = -1;
        string? winningHash = null;
        var stopwatch = Stopwatch.StartNew();
        using var statisticsCancellation = new CancellationTokenSource();

        Task statisticsTask = reportProgress is null
            ? Task.CompletedTask
            : ReportStatisticsAsync(
                () => Interlocked.Read(ref attempts),
                () => Interlocked.Read(ref currentNonce),
                stopwatch,
                reportProgress,
                statisticsCancellation.Token);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = cancellationToken
        };

        try
        {
            await Task.Run(
                () => Parallel.ForEach(
                    Partitioner.Create(0L, long.MaxValue, RangeSize),
                    options,
                    (range, loopState) =>
                    {
                        long localAttempts = 0;
                        long latestLocalNonce = range.Item1;

                        try
                        {
                            for (long nonce = range.Item1; nonce < range.Item2; nonce++)
                            {
                                if (loopState.ShouldExitCurrentIteration)
                                {
                                    return;
                                }

                                string hash = _hashingService.ComputeHash(blockPrefix + nonce);
                                localAttempts++;
                                latestLocalNonce = nonce;

                                if (localAttempts >= AttemptBatchSize)
                                {
                                    PublishLocalStatistics(
                                        ref attempts,
                                        ref currentNonce,
                                        ref localAttempts,
                                        latestLocalNonce);
                                }

                                if (!hash.StartsWith(targetPrefix, StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                if (Interlocked.CompareExchange(ref winningNonce, nonce, -1) == -1)
                                {
                                    winningHash = hash;
                                    loopState.Stop();
                                }

                                return;
                            }
                        }
                        finally
                        {
                            PublishLocalStatistics(
                                ref attempts,
                                ref currentNonce,
                                ref localAttempts,
                                latestLocalNonce);
                        }
                    }),
                cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            statisticsCancellation.Cancel();

            try
            {
                await statisticsTask;
            }
            catch (OperationCanceledException) when (statisticsCancellation.IsCancellationRequested)
            {
                // Cancellation is the normal way the live statistics monitor is stopped.
            }
        }

        if (winningNonce < 0 || winningHash is null)
        {
            throw new InvalidOperationException("No matching nonce was found.");
        }

        block.Nonce = winningNonce;
        block.Hash = winningHash;

        var (hashRate, measureUnitIndex, timeTaken) =
            CalculateStatistics(Interlocked.Read(ref attempts), stopwatch.Elapsed);

        reportProgress?.Invoke(new MiningProgressSnapshot(
            Interlocked.Read(ref attempts),
            winningNonce,
            hashRate,
            MeasureUnits[measureUnitIndex],
            stopwatch.Elapsed,
            timeTaken));

        return new MiningResult(
            hashRate,
            measureUnitIndex,
            timeTaken,
            winningNonce,
            Interlocked.Read(ref attempts));
    }

    private static void PublishLocalStatistics(
        ref long attempts,
        ref long currentNonce,
        ref long localAttempts,
        long latestLocalNonce)
    {
        if (localAttempts == 0)
        {
            return;
        }

        Interlocked.Add(ref attempts, localAttempts);
        Interlocked.Exchange(ref currentNonce, latestLocalNonce);
        localAttempts = 0;
    }

    private static async Task ReportStatisticsAsync(
        Func<long> getAttempts,
        Func<long> getCurrentNonce,
        Stopwatch stopwatch,
        Action<MiningProgressSnapshot> reportProgress,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long attempts = getAttempts();
            var (hashRate, measureUnitIndex, timeTaken) =
                CalculateStatistics(attempts, stopwatch.Elapsed);

            reportProgress(new MiningProgressSnapshot(
                attempts,
                getCurrentNonce(),
                hashRate,
                MeasureUnits[measureUnitIndex],
                stopwatch.Elapsed,
                timeTaken));

            await Task.Delay(100, cancellationToken);
        }
    }

    private static void ValidateMiningArguments(Block block, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfNegative(difficulty);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(difficulty, 64);
    }

    internal static string FormatElapsed(TimeSpan elapsed)
    {
        string daysTaken = elapsed.Days != 0 ? elapsed.Days + "d " : string.Empty;
        string hoursTaken = elapsed.Hours != 0 || elapsed.Days != 0 ? elapsed.Hours + "h " : string.Empty;
        string minutesTaken = elapsed.Minutes != 0 || elapsed.Hours != 0 || elapsed.Days != 0
            ? elapsed.Minutes + "m "
            : string.Empty;

        return $"{daysTaken}{hoursTaken}{minutesTaken}{elapsed.Seconds}.{elapsed.Milliseconds:000}s";
    }

    private static (double hashRate, short measureUnitIndex, string timeTaken) CalculateStatistics(
        long attempts,
        TimeSpan elapsed)
    {
        short measureUnitIndex = 0;
        double hashRate = attempts / Math.Max(elapsed.TotalSeconds, double.Epsilon);

        while (hashRate >= 1000 && measureUnitIndex < MeasureUnits.Length - 1)
        {
            hashRate /= 1000;
            measureUnitIndex++;
        }

        return (hashRate, measureUnitIndex, FormatElapsed(elapsed));
    }

    private static void WriteMiningResult(MiningResult result)
    {
        Console.WriteLine(
            $"Average hashrate: {result.HashRate:F2} {MeasureUnits[result.MeasureUnitIndex]}H/s, " +
            $"Nonce: {result.Nonce}, Attempts: {result.Attempts:N0}, Time taken: {result.TimeTaken}");
    }

    public void TestMiningEfficiency(short maxDifficulty)
    {
        Table efficiencyTable = new Table()
            .Title("Mining Efficiency Test", new Style(foreground: Color.DeepSkyBlue1))
            .Caption("Waiting for mining to complete...", new Style(foreground: Color.Gray, decoration: Decoration.Dim))
            .Border(TableBorder.Rounded)
            .BorderColor(Color.DeepSkyBlue1)
            .AddColumn(new TableColumn("[b]Status[/]").Centered())
            .AddColumn(new TableColumn("[b]Difficulty[/]").Centered())
            .AddColumn(new TableColumn("[b]Nonce[/]").Centered())
            .AddColumn(new TableColumn("[b]Avg Hash Rate[/]").Centered())
            .AddColumn(new TableColumn("[b]Time Taken[/]").Centered());

        for (short i = 1; i <= maxDifficulty; i++)
        {
            efficiencyTable.AddRow("[red]Waiting...[/]", i.ToString(), "[dim]-[/]", "[dim]-[/]", "[dim]-[/]");
        }

        AnsiConsole.Live(efficiencyTable)
            .Start(ctx =>
            {
                for (short i = 1; i <= maxDifficulty; i++)
                {
                    efficiencyTable.UpdateCell(i - 1, 0, "[yellow]Mining...[/]");
                    ctx.Refresh();

                    var (hashRate, measureUnit, timeTaken, winningNonce) =
                        MineBlock(new Block(-1, [], "Test Block", i), i, false);

                    efficiencyTable.UpdateCell(i - 1, 0, "[green]Completed![/]");
                    efficiencyTable.UpdateCell(i - 1, 2, $"{winningNonce:N0}");
                    efficiencyTable.UpdateCell(i - 1, 3, $"{hashRate:F2} {measureUnit}H/s");
                    efficiencyTable.UpdateCell(i - 1, 4, timeTaken);
                    ctx.Refresh();
                }

                efficiencyTable.Caption("Finished!", new Style(foreground: Color.Gray, decoration: Decoration.Dim));
                ctx.Refresh();
            });
    }

    private readonly record struct MiningResult(
        double HashRate,
        short MeasureUnitIndex,
        string TimeTaken,
        long Nonce,
        long Attempts);
}
