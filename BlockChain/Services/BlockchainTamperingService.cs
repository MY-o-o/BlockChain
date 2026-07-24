using BlockChain.Components;
using BlockChain.Models;
using Spectre.Console;
using System.Diagnostics;

namespace BlockChain.Services;

public sealed class BlockchainTamperingService
{
    private readonly MiningService _miningService;

    public BlockchainTamperingService(MiningService miningService)
    {
        _miningService = miningService;
    }

    public Task<TimeSpan> HackChain(
        BlockChainService blockChainService,
        int blockIndex,
        Transaction forgedTransaction,
        bool showProgress = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(forgedTransaction);

        return HackChain(
            blockChainService,
            blockIndex,
            [forgedTransaction],
            showProgress,
            cancellationToken);
    }

    public async Task<TimeSpan> HackChain(
        BlockChainService blockChainService,
        int blockIndex,
        IEnumerable<Transaction> forgedTransactions,
        bool showProgress = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blockChainService);
        ArgumentNullException.ThrowIfNull(forgedTransactions);

        if (blockIndex < 0 || blockIndex >= blockChainService.Chain.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blockIndex),
                blockIndex,
                $"Block index must be between 0 and {blockChainService.Chain.Count - 1}.");
        }

        List<Transaction> transactionsToInject = forgedTransactions
            .Select(transaction =>
            {
                ArgumentNullException.ThrowIfNull(transaction);
                return (Transaction)transaction.Clone();
            })
            .ToList();

        if (transactionsToInject.Count == 0)
        {
            throw new ArgumentException(
                "At least one forged transaction must be supplied.",
                nameof(forgedTransactions));
        }

        if (!showProgress)
        {
            return await HackChainCoreAsync(
                blockChainService,
                blockIndex,
                transactionsToInject,
                null,
                cancellationToken);
        }

        return await HackChainWithProgressAsync(
            blockChainService,
            blockIndex,
            transactionsToInject,
            cancellationToken);
    }

    private async Task<TimeSpan> HackChainWithProgressAsync(
        BlockChainService blockChainService,
        int blockIndex,
        IReadOnlyCollection<Transaction> transactionsToInject,
        CancellationToken cancellationToken)
    {
        TimeSpan totalElapsed = TimeSpan.Zero;
        var display = AnsiConsole.Progress()
            .Columns(
                new SpinnerColumn(new MinerSpinner())
                {
                    Style = new Style(Color.Orange1)
                },
                new TaskDescriptionColumn
                {
                    Wrap = true,
                    Alignment = Justify.Left
                },
                new ProgressBarColumn(),
                new PercentageColumn())
            .AutoRefresh(false)
            .AutoClear(false)
            .HideCompleted(false);

        await display.StartAsync(async context =>
        {
            int affectedBlockCount = blockChainService.Chain.Count - blockIndex;
            var overallTask = context.AddTask(
                $"[bold deepskyblue1]Overall:[/] 0/{affectedBlockCount} blocks | [cyan]0.000s[/]",
                maxValue: affectedBlockCount);

            var blockTasks = new Dictionary<int, ProgressTask>();
            for (int position = blockIndex; position < blockChainService.Chain.Count; position++)
            {
                Block block = blockChainService.Chain[position];
                ProgressTask task = context.AddTask(
                    $"[grey]Waiting[/] | Block #{block.Index} | Nonce: - | Hash rate: - | Time: -",
                    maxValue: 100);
                blockTasks.Add(position, task);
            }

            context.Refresh();

            totalElapsed = await HackChainCoreAsync(
                blockChainService,
                blockIndex,
                transactionsToInject,
                new TamperingProgress(
                    overallTask,
                    blockTasks,
                    context,
                    affectedBlockCount),
                cancellationToken);
        });

        AnsiConsole.MarkupLine(
            $"[bold green]Chain hack completed in {Markup.Escape(MiningService.FormatElapsed(totalElapsed))}.[/]");

        return totalElapsed;
    }

    private async Task<TimeSpan> HackChainCoreAsync(
        BlockChainService blockChainService,
        int blockIndex,
        IReadOnlyCollection<Transaction> transactionsToInject,
        TamperingProgress? progress,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Block tamperedBlock = blockChainService.Chain[blockIndex];
        tamperedBlock.Transactions.AddRange(transactionsToInject);

        for (int position = blockIndex; position < blockChainService.Chain.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Block block = blockChainService.Chain[position];
            if (position > blockIndex)
            {
                block.PrevHash = blockChainService.Chain[position - 1].Hash;
            }

            block.Nonce = 0;
            block.Hash = string.Empty;
            progress?.StartBlock(position, block.Index, totalStopwatch.Elapsed);

            var blockStopwatch = Stopwatch.StartNew();
            var (hashRate, measureUnit, timeTaken, winningNonce) =
                await _miningService.MineBlockAsync(
                    block,
                    block.Difficulty,
                    showProgress: false,
                    snapshot => progress?.UpdateBlock(
                        position,
                        block.Index,
                        snapshot,
                        totalStopwatch.Elapsed),
                    cancellationToken);
            blockStopwatch.Stop();

            progress?.CompleteBlock(
                position,
                block.Index,
                winningNonce,
                hashRate,
                measureUnit,
                timeTaken,
                totalStopwatch.Elapsed);
        }

        totalStopwatch.Stop();
        progress?.Complete(totalStopwatch.Elapsed);
        return totalStopwatch.Elapsed;
    }

    private sealed class TamperingProgress(
        ProgressTask overallTask,
        IReadOnlyDictionary<int, ProgressTask> blockTasks,
        ProgressContext context,
        int affectedBlockCount)
    {
        private int _completedBlocks;

        public void StartBlock(int position, int blockIndex, TimeSpan totalElapsed)
        {
            ProgressTask task = blockTasks[position];
            task.IsIndeterminate = true;
            task.Description =
                $"[yellow]Re-mining[/] | Block #{blockIndex} | Nonce: 0 | Hash rate: - | Time: 0.000s";

            UpdateOverall(totalElapsed);
            context.Refresh();
        }

        public void UpdateBlock(
            int position,
            int blockIndex,
            MiningProgressSnapshot snapshot,
            TimeSpan totalElapsed)
        {
            ProgressTask task = blockTasks[position];
            task.Description =
                $"[yellow]Re-mining[/] | Block #{blockIndex} | Nonce: {snapshot.CurrentNonce:N0} | " +
                $"Hash rate: {snapshot.HashRate:F2} {snapshot.MeasureUnit}H/s | " +
                $"Time: {Markup.Escape(snapshot.FormattedElapsed)}";

            UpdateOverall(totalElapsed);
            context.Refresh();
        }

        public void CompleteBlock(
            int position,
            int blockIndex,
            long nonce,
            double hashRate,
            char measureUnit,
            string timeTaken,
            TimeSpan totalElapsed)
        {
            ProgressTask task = blockTasks[position];
            task.IsIndeterminate = false;
            task.Value = 100;
            task.Description =
                $"[green]Completed[/] | Block #{blockIndex} | Nonce: {nonce:N0} | " +
                $"Hash rate: {hashRate:F2} {measureUnit}H/s | Time: {Markup.Escape(timeTaken)}";

            _completedBlocks++;
            overallTask.Increment(1);
            UpdateOverall(totalElapsed);
            context.Refresh();
        }

        public void Complete(TimeSpan totalElapsed)
        {
            overallTask.Value = overallTask.MaxValue;
            overallTask.Description =
                $"[bold green]Overall: {_completedBlocks}/{affectedBlockCount} blocks completed | " +
                $"{Markup.Escape(MiningService.FormatElapsed(totalElapsed))}[/]";
            context.Refresh();
        }

        private void UpdateOverall(TimeSpan totalElapsed)
        {
            overallTask.Description =
                $"[bold deepskyblue1]Overall:[/] {_completedBlocks}/{affectedBlockCount} blocks | " +
                $"[cyan]{Markup.Escape(MiningService.FormatElapsed(totalElapsed))}[/]";
        }
    }
}
