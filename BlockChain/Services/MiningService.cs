using BlockChain.Components;
using BlockChain.Models;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Transactions;

namespace BlockChain.Services;

public class MiningService
{
    private const long RangeSize = 50_000;
    private static readonly char[] MeasureUnits = [' ', 'k', 'M', 'G', 'T', 'P', 'E'];
    private readonly HashingService _hashingService;

    public MiningService(HashingService hashingService)
    {
        _hashingService = hashingService;
    }

    public (double hashRate, char measureUnit, string timeTaken, long nonce) MineBlock(Block block, int difficulty, bool showProgress = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(difficulty);
        ArgumentNullException.ThrowIfNull(block);

        string targetPrefix = new string('0', difficulty);

        string transactionsRow = string.Concat(block.Transactions.Select(t => t.ToRowString()));
        string blockPrefix = $"{block.Index}{block.TimeStamp:o}{transactionsRow}{block.PrevHash}{block.Difficulty}";

        long attempts = 0;
        long winningNonce = -1;
        string? winningHash = null;
        var stopwatch = Stopwatch.StartNew();
        using var statisticsCancellation = new CancellationTokenSource();
        var statisticsTask = showProgress
            ? ShowStatisticsAsync(() => Interlocked.Read(ref attempts), stopwatch, statisticsCancellation.Token)
            : Task.CompletedTask;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        try
        {
            Parallel.ForEach(
                Partitioner.Create(0L, long.MaxValue, RangeSize),
                options,
                (range, loopState) =>
                {
                    // Publish batches instead of incrementing one contended global counter
                    // for every hash. The monitor only needs an approximate live total.
                    long localAttempts = 0;
                    try
                    {
                        for (var nonce = range.Item1; nonce < range.Item2; nonce++)
                        {
                            if (loopState.ShouldExitCurrentIteration)
                            {
                                return;
                            }

                            var hash = _hashingService.ComputeHash(blockPrefix + nonce);
                            localAttempts++;

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
                        Interlocked.Add(ref attempts, localAttempts);
                    }
                });
        }
        finally
        {
            stopwatch.Stop();
            statisticsCancellation.Cancel();
            statisticsTask.GetAwaiter().GetResult();
        }

        // A completed parallel loop can only occur without a winner on nonce overflow.
        if (winningNonce < 0 || winningHash is null)
        {
            throw new InvalidOperationException("No matching nonce was found.");
        }

        block.Nonce = winningNonce;
        block.Hash = winningHash;

        var (hashRate, measureUnitIndex, timeTaken) = CalculateStatistics(attempts, stopwatch.Elapsed);
        if (showProgress)
        {
            WriteMiningResult(hashRate, measureUnitIndex, timeTaken, winningNonce, attempts);
        }

        return (hashRate, MeasureUnits[measureUnitIndex], timeTaken, winningNonce);
    }

    private static (double hashRate, short measureUnitIndex, string timeTaken) CalculateStatistics(long attempts, TimeSpan elapsed)
    {
        short measureUnitIndex = 0;
        double hashRate = attempts / Math.Max(elapsed.TotalSeconds, double.Epsilon);
        for (measureUnitIndex = 0; hashRate >= 1000 && measureUnitIndex < MeasureUnits.Length - 1; measureUnitIndex++)
        {
            hashRate /= 1000;
        }

        string daysTaken = elapsed.Days != 0 ? elapsed.Days + "d " : string.Empty;
        string hoursTaken = elapsed.Hours != 0 || elapsed.Days != 0 ? elapsed.Hours + "h " : string.Empty;
        string minutesTaken = elapsed.Minutes != 0 || elapsed.Hours != 0 || elapsed.Days != 0 ? elapsed.Minutes + "m " : string.Empty;
        string timeTaken = $"{daysTaken}{hoursTaken}{minutesTaken}{elapsed.Seconds}.{elapsed.Milliseconds:000}s";

        return (hashRate, measureUnitIndex, timeTaken);
    }

    private static void WriteMiningResult(double hashRate, short measureUnitIndex, string timeTaken, long nonce, long attempts)
    {
        Console.WriteLine($"Average hashrate: {hashRate:F2} {MeasureUnits[measureUnitIndex]}H/s, Nonce: {nonce}, Attempts: {attempts:N0}, Time taken: {timeTaken}");
    }

    private static async Task ShowStatisticsAsync(
        Func<long> getAttempts,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        try
        {
            var progress = AnsiConsole.Progress()
            .Columns(
                new SpinnerColumn(new MinerSpinner())
                {
                    Style = new Style(Color.Orange1)
                },
                new TaskDescriptionColumn()
                {
                    Wrap = true,
                    Alignment = Justify.Left
                });
            progress.AutoRefresh = false;
            progress.AutoClear = true;

            await progress.StartAsync(async ctx =>
            {
                var task = ctx.AddTask("⛏ [bold orange1]Mining[/] [grey]in progress...[/]", maxValue: 100);
                var task2 = ctx.AddTask("[bold cyan]▣ Statistics[/]", maxValue: 0);

                while (!cancellationToken.IsCancellationRequested)
                {
                    long attempts = getAttempts();
                    var (hashRate, measureUnitIndex, timeTaken) =
                        CalculateStatistics(attempts, stopwatch.Elapsed);

                    task2.Description =
                        $"[gray][bold green]{hashRate:F2} {MeasureUnits[measureUnitIndex]}H/s[/] | [bold white]{attempts:N0} attempts[/] | [bold cyan]{timeTaken}[/][/]";

                    ctx.Refresh();
                    await Task.Delay(100, cancellationToken);
                }
            });
        }
        catch (OperationCanceledException)
        {
            Console.Clear();
            Console.WriteLine("Mining completed.");
        }
    }

    public void TestMiningEffieciency(short maxDifficulty)
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

                    var (hashRate, measureUnit, timeTaken, winningNonce) = MineBlock(new Block(-1, [], "Test Block", i), i, false);

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
}
