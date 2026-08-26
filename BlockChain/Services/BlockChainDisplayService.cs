using BlockChain.Models;
using Spectre.Console;

namespace BlockChain.Services;

public static class BlockChainDisplayService
{
    public static void DisplayBlockChain(IEnumerable<Block> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var blocks = chain.ToList();
        if (blocks.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]The blockchain is empty.[/]");
            return;
        }

        AnsiConsole.Write(new Rule("[bold deepskyblue1]Blockchain[/]").RuleStyle("grey"));

        for (var blockNumber = 0; blockNumber < blocks.Count; blockNumber++)
        {
            var block = blocks[blockNumber];
            var content = new Rows(
                CreateBlockTable(block),
                new Rule("[grey]Transactions[/]").RuleStyle("grey"),
                CreateTransactionsTable(block.Transactions));

            AnsiConsole.Write(
                new Panel(content)
                    .Header($"[bold]Block {block.Index}[/] ({blockNumber + 1}/{blocks.Count})")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.SteelBlue1)
                    .Padding(1, 0));

            if (blockNumber < blocks.Count - 1)
            {
                AnsiConsole.WriteLine();
            }
        }
    }

    public static void DisplayTransactions(IEnumerable<Transaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var transactionList = transactions.ToList();
        if (transactionList.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No transactions to display.[/]");
            return;
        }

        AnsiConsole.Write(CreateTransactionsTable(transactionList));
    }

    public static void DisplayValidationResult(bool isValid)
    {
        var status = isValid
            ? new Panel("[bold green]The blockchain is valid.[/]")
                .Header("[green]Validation succeeded[/]")
                .BorderColor(Color.Green)
            : new Panel("[bold red]The blockchain is NOT valid.[/]")
                .Header("[red]Validation failed[/]")
                .BorderColor(Color.Red);

        AnsiConsole.Write(status.Border(BoxBorder.Rounded).Padding(1, 0));
    }

    public static void DisplayBlockchainSpecs(BlockChainService blockChainService)
    {
        ArgumentNullException.ThrowIfNull(blockChainService);
        var specsTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Property").Width(25))
            .AddColumn(new TableColumn("Value").Width(16));
        AddProperty(specsTable, "Listen Port", blockChainService.NodeListenPort.ToString());
        AddProperty(specsTable, "Send Port", blockChainService.NodeSendPort.ToString());
        var chain = blockChainService.GetChainSnapshot();
        var pendingTransactions = blockChainService.GetPendingTransactionsSnapshot();
        AddProperty(specsTable, "Blocks in Chain", chain.Count.ToString());
        AddProperty(specsTable, "Transactions in Mempool", pendingTransactions.Count.ToString());
        AddProperty(specsTable, "Difficulty", blockChainService.Difficulty.ToString());
        AddProperty(specsTable, "Last Hash", chain.Last().Hash);
        AnsiConsole.Write(
            new Panel(specsTable)
                .Header("[bold]Blockchain Specifications[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.SteelBlue1)
                .Padding(1, 0));
    }

    private static Table CreateBlockTable(Block block)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Property").Width(16))
            .AddColumn(new TableColumn("Value"));

        AddProperty(table, "Index", block.Index.ToString());
        AddProperty(table, "Timestamp", block.TimeStamp.ToString("O"));
        AddProperty(table, "Previous hash", block.PrevHash);
        AddProperty(table, "Difficulty", block.Difficulty.ToString());
        AddProperty(table, "Nonce", block.Nonce.ToString());
        AddProperty(table, "Hash", block.Hash);

        return table;
    }

    private static Table CreateTransactionsTable(IEnumerable<Transaction> transactions)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .ShowRowSeparators()
            .Title("[bold]Transactions[/]")
            .AddColumn(new TableColumn("ID").NoWrap())
            .AddColumn(new TableColumn("From"))
            .AddColumn(new TableColumn("To"))
            .AddColumn(new TableColumn("Amount").RightAligned())
            .AddColumn(new TableColumn("Fee").RightAligned())
            .AddColumn(new TableColumn("Timestamp"));

        foreach (var transaction in transactions)
        {
            table.AddRow(
                Escape(transaction.Id.ToString()),
                Escape(transaction.From),
                Escape(transaction.To),
                Escape(transaction.Amount.ToString()),
                Escape(transaction.Fee.ToString()),
                Escape(transaction.TimeStamp.ToString("O")));
        }

        return table;
    }

    private static void AddProperty(Table table, string property, string? value)
    {
        table.AddRow($"[bold grey]{Escape(property)}[/]", Escape(value));
    }

    private static string Escape(string? value) => Markup.Escape(value ?? string.Empty);
}
