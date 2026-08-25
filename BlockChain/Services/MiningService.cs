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

public readonly record struct MiningResult(
        double HashRate,
        short MeasureUnitIndex,
        string TimeTaken,
        long Nonce,
        long Attempts);

public sealed class MiningService
{
    private readonly Lock _miningLock = new();
    private CancellationTokenSource? _activeMiningCancellation;
    public static readonly char[] MeasureUnits = [' ', 'k', 'M', 'G', 'T', 'P', 'E'];
    private const long _nonceRangeSize = 50_000;
    private const long _statisticsBatchSize = 50_000;
    private const int _statisticsIntervalMilliseconds = 100;
    private const int _maximumDifficulty = 64;

    public MiningResult MineBlock(
        Block block,
        int difficulty,
        bool showProgress = true,
        ConsoleKey cancelKey = ConsoleKey.None)
    {
        return MineBlockAsync(block, difficulty, showProgress, cancelKey: cancelKey).GetAwaiter().GetResult();
    }

    public async Task<MiningResult> MineBlockAsync(
        Block block,
        int difficulty,
        bool showProgress = true,
        Action<MiningProgressSnapshot>? reportProgress = null,
        ConsoleKey cancelKey = ConsoleKey.Escape,
        CancellationToken cancellationToken = default)
    {
        ValidateMiningArguments(block, difficulty);

        using var miningCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        RegisterActiveMining(miningCancellation);

        using var keyListenerCancellation = new CancellationTokenSource();

        Task<MiningResult> miningTask = showProgress
            ? MineWithProgressAsync(block, difficulty, reportProgress, cancelKey, miningCancellation.Token)
            : MineCoreAsync(block, difficulty, reportProgress, miningCancellation.Token);

        Task keyListenerTask = ListenForCancelKeyAsync(cancelKey, miningTask, miningCancellation, keyListenerCancellation.Token);

        try
        {
            return await miningTask;
        }
        finally
        {
            keyListenerCancellation.Cancel();
            UnregisterActiveMining(miningCancellation);

            try
            {
                await keyListenerTask;
            }
            catch (OperationCanceledException) when (keyListenerCancellation.IsCancellationRequested)
            {
                // The key listener is stopped when mining completes or is cancelled.
            }
        }
    }

    private static async Task ListenForCancelKeyAsync(
        ConsoleKey cancelKey,
        Task miningTask,
        CancellationTokenSource miningCancellation,
        CancellationToken listenerCancellation)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        try
        {
            while (!miningTask.IsCompleted)
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == cancelKey)
                {
                    miningCancellation.Cancel();
                    return;
                }

                await Task.Delay(_statisticsIntervalMilliseconds, listenerCancellation);
            }
        }
        catch (InvalidOperationException)
        {
            // Console input is unavailable in this host.
        }
    }

    private static async Task<MiningResult> MineWithProgressAsync(
        Block block,
        int difficulty,
        Action<MiningProgressSnapshot>? externalProgress,
        ConsoleKey cancelTextKey,
        CancellationToken cancellationToken)
    {
        MiningResult result = default;
        bool cancelled = false;

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

            var cancelTextTask = context.AddTask(
                $"[grey]Press [dim bold white]{cancelTextKey}[/] to cancel mining[/]",
                maxValue: 0);
            cancelTextTask.IsIndeterminate = true;

            try
            {
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
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                miningTask.IsIndeterminate = false;
                miningTask.Value = 100;
                miningTask.Description = $"[bold yellow]Cancelled block #{block.Index}[/]";

                statisticsTask.IsIndeterminate = false;
                statisticsTask.Value = 100;
                statisticsTask.Description = "[bold yellow]Statistics: mining cancelled[/]";

                cancelTextTask.IsIndeterminate = false;
                cancelTextTask.Value = 100;
                cancelTextTask.Description = "[grey]Mining cancelled[/]";
                context.Refresh();
            }

            if (!cancelled)
            {
                miningTask.IsIndeterminate = false;
                miningTask.Value = 100;
                miningTask.Description = $"[bold green]Completed block #{block.Index}[/]";

                statisticsTask.IsIndeterminate = false;
                statisticsTask.Value = 100;
                cancelTextTask.IsIndeterminate = false;
                cancelTextTask.Value = 100;
            }
            context.Refresh();
        });

        if (cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        return result;
    }

    private static async Task<MiningResult> MineCoreAsync(
        Block block,
        int difficulty,
        Action<MiningProgressSnapshot>? reportProgress,
        CancellationToken cancellationToken)
    {
        block.Difficulty = difficulty;

        string targetPrefix = new('0', difficulty);
        string blockPrefix = block.ToRowString(includeNonce: false);

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
                    Partitioner.Create(0L, long.MaxValue, _nonceRangeSize),
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

                                string hash = HashingService.ComputeHash(blockPrefix + nonce);
                                localAttempts++;
                                latestLocalNonce = nonce;

                                if (localAttempts >= _statisticsBatchSize)
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
                                    Volatile.Write(ref winningHash, hash);
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

        string? completedHash = Volatile.Read(ref winningHash);
        if (winningNonce < 0 || completedHash is null)
        {
            throw new InvalidOperationException("No matching nonce was found.");
        }

        block.Nonce = winningNonce;
        block.Hash = completedHash;

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

    public void CancelMining()
    {
        lock (_miningLock)
        {
            _activeMiningCancellation?.Cancel();
        }
    }

    private void RegisterActiveMining(CancellationTokenSource cancellation)
    {
        lock (_miningLock)
        {
            if (_activeMiningCancellation is not null)
            {
                throw new InvalidOperationException("Mining is already in progress.");
            }

            _activeMiningCancellation = cancellation;
        }
    }

    private void UnregisterActiveMining(CancellationTokenSource cancellation)
    {
        lock (_miningLock)
        {
            if (ReferenceEquals(_activeMiningCancellation, cancellation))
            {
                _activeMiningCancellation = null;
            }
        }
    }

    private static void PublishLocalStatistics(
        ref long attempts,
        ref long currentNonce,
        ref long localAttempts,
        long latestLocalNonce)
    {
        if (localAttempts == 0) return;
        
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

            await Task.Delay(_statisticsIntervalMilliseconds, cancellationToken);
        }
    }

    private static void ValidateMiningArguments(Block block, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfNegative(difficulty);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(difficulty, _maximumDifficulty);
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

    public static void DisplayMiningResult(MiningResult result)
    {
        string message = $"[bold]Average Hash Rate:[/] [green]{result.HashRate:F2} {MeasureUnits[result.MeasureUnitIndex]}H/s[/], " +
                         $"[bold]Nonce:[/] {result.Nonce}\n" +
                         $"[bold]Attempts:[/] {result.Attempts:N0}, " +
                         $"[bold]Time Taken:[/] [cyan]{result.TimeTaken}[/]";
        var panel = new Panel(message)
            .Header("[green]Mining completed![/]")
            .BorderColor(Color.Green4)
            .Border(BoxBorder.Rounded)
            .Padding(2 , 0);
        AnsiConsole.Write(panel);
    }

    public async Task TestMiningEfficiencyAsync(int maxDifficulty, ConsoleKey cancelKey = ConsoleKey.Escape)
    {
        Table efficiencyTable = new Table()
            .Title("Mining Efficiency Test", new Style(foreground: Color.Orange1))
            .Caption($"Press ({cancelKey}) to cancel", new Style(foreground: Color.Gray, decoration: Decoration.Dim))
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Orange3)
            .AddColumn(new TableColumn("[b]Status[/]").Centered())
            .AddColumn(new TableColumn("[b]Difficulty[/]").Centered())
            .AddColumn(new TableColumn("[b]Nonce[/]").Centered())
            .AddColumn(new TableColumn("[b]Avg Hash Rate[/]").Centered())
            .AddColumn(new TableColumn("[b]Time Taken[/]").Centered());

        for (int i = 1; i <= maxDifficulty; i++)
        {
            efficiencyTable.AddRow("[red]Waiting...[/]", i.ToString(), "[dim]-[/]", "[dim]-[/]", "[dim]-[/]");
        }

        await AnsiConsole.Live(efficiencyTable)
            .StartAsync(async ctx =>
            {
                for (short i = 1; i <= maxDifficulty; i++)
                {
                    efficiencyTable.UpdateCell(i - 1, 0, "[yellow]Mining...[/]");
                    ctx.Refresh();

                    var (hashRate, measureUnitIndex, timeTaken, winningNonce, _) =
                        await MineBlockAsync(new Block(-1, [], "Test Block", i), i, false, cancelKey: cancelKey);

                    efficiencyTable.UpdateCell(i - 1, 0, "[green]Completed![/]");
                    efficiencyTable.UpdateCell(i - 1, 2, $"{winningNonce:N0}");
                    efficiencyTable.UpdateCell(i - 1, 3, $"{hashRate:F2} {MeasureUnits[measureUnitIndex]}H/s");
                    efficiencyTable.UpdateCell(i - 1, 4, timeTaken);
                    ctx.Refresh();
                }

                efficiencyTable.Caption("Finished!", new Style(foreground: Color.Gray, decoration: Decoration.Dim));
                ctx.Refresh();
            });
    }
}
