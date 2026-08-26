using BlockChain.Models;
using BlockChain.Services;
using Spectre.Console;
using System.Text;

using UI = BlockChain.Services.UIUXService;

Console.OutputEncoding = Encoding.UTF8;

// Usage: dotnet run -- <listenPort> <peerPort>
// Example pair: node A: 533 534, node B: 534 533.
int nodeListenPort = args.Length > 0 && int.TryParse(args[0], out var parsedListenPort) ? parsedListenPort : 533;
int nodePeerPort = args.Length > 1 && int.TryParse(args[1], out var parsedPeerPort) ? parsedPeerPort : 534;

//TODO: Implement DI
var walletService = new WalletService();
var miningService = new MiningService();
var tamperingService = new BlockchainTamperingService(miningService);
var blockChainService = new BlockChainService(miningService, chainFilePath: $"blockchain-{nodeListenPort}.json")
{
    NodeListenPort = nodeListenPort,
    NodeSendPort = nodePeerPort,
};
await blockChainService.StartBackgroundSyncAsync([new NetworkEndpoint("127.0.0.1", blockChainService.NodeSendPort)]);

// tmp code
var myWallet = WalletService.CreateWallet("You");
var bobWallet = WalletService.CreateWallet("Bob");
// end of tmp code

while (true)
{
    var menuOptions = new (short, string)[]
    {
        ( 1, "Add a new block" ),
        ( 2, "Display the blockchain" ),
        ( 3, "Validate the blockchain" ),
        //( 4, "Tamper with a block" ),
        ( 6, "Print account info" ),
        ( 7, "Transfer coins" ),
        ( 8, "Display pending transactions" ),
        ( 9, "Test mining efficiency" ),
        ( 10, "Synchronize blockchain with peer" ),
        ( 11, "Display total blockchain supply" ),
        ( 12, "Send pending transaction to peer" ),
        ( 13, "Send last block to peer" ),
        ( 14, "Display node specs" ),
        ( 15, "Change node ports"),
        ( 16, "Exit" )
    };

    var prompt = new SelectionPrompt<(short, string)>()
        .Title("[bold orange1]Block Management Menu[/]:")
        .WrapAround()
        .PageSize(20)
        .MoreChoicesText("[grey](Move up and down to reveal more options)[/]")
        .EnableSearch()
        .SearchPlaceholderText("[grey](Type to search...)[/]")
        .UseConverter(option => option.Item2)
        .AddChoices(menuOptions);
    prompt.HighlightStyle = new Style(foreground: Color.Orange3, decoration: Decoration.Bold);
    prompt.SearchHighlightStyle = new Style(foreground: Color.Orange1, decoration: Decoration.Underline);

    var selectedOption = AnsiConsole.Prompt(prompt);

    switch (selectedOption.Item1)
    {
        case 1:
            try
            {
                var result = await blockChainService.MinePendingTransactionsAsync(myWallet.Address);
                MiningService.DisplayMiningResult(result);
            }
            catch (Exception ex)
            {
                UI.ErrorPrint(ex.Message);
            }
            break;
        case 2:
            BlockChainDisplayService.DisplayBlockChain(blockChainService.Chain);
            break;
        case 3:
            BlockChainDisplayService.DisplayValidationResult(blockChainService.IsValidChain(blockChainService.Chain));
            break;
        //case 4:
            //TODO: Implement a better UI/UX input and upgrade the tampering service
            //Console.Write($"Enter block index to tamper with (0-{blockChainService.Chain.Count - 1}): ");
            //if (!int.TryParse(Console.ReadLine(), out int blockIndex))
            //{
            //    UI.ErrorPrint("Invalid block index.");
            //    break;
            //}

            //Console.Write("Forged sender: ");
            //string forgedSender = Console.ReadLine() ?? string.Empty;
            //Console.Write("Forged recipient: ");
            //string forgedRecipient = Console.ReadLine() ?? string.Empty;
            //Console.Write("Forged amount: ");

            //if (!decimal.TryParse(Console.ReadLine(), out decimal forgedAmount))
            //{
            //    UI.ErrorPrint("Invalid amount.");
            //    break;
            //}

            //try
            //{
            //    var forgedTransaction = new Transaction(
            //        forgedSender,
            //        forgedRecipient,
            //        forgedAmount,
            //        0);

            //    await tamperingService.HackChain(
            //        blockChainService,
            //        blockIndex,
            //        forgedTransaction);

            //    BlockChainDisplayService.DisplayValidationResult(blockChainService.IsValidChain(blockChainService.Chain));
            //}
            //catch (ArgumentException exception)
            //{
            //    UI.ErrorPrint(exception.Message);
            //}
            //break;
        case 6:
            BlockChainDisplayService.PrintAccountStatement(blockChainService, myWallet.Address);
            break;
        case 7:
            try
            {
                //TODO: Implement more sophisticated receiver selection
                var receivers = new (string, string)[]
                {
                    (bobWallet.Alias, bobWallet.Address),
                    (myWallet.Alias, myWallet.Address),
                    ("BURN", "BURN")
                };

                var receiverPrompt = new SelectionPrompt<(string alias, string address)>()
                .Title("[bold orange1]Choose the receiver:[/]")
                .MoreChoicesText("[grey dim](Move up and down to reveal more options)[/]")
                .EnableSearch()
                .SearchPlaceholderText("[grey dim](Type to search...)[/]")
                .UseConverter(r => $"{r.alias} ({(r.address.Length >= 20 ? "..." + r.address[^20..] : r.address)})")
                .AddChoices(receivers);
                receiverPrompt.HighlightStyle = new Style(foreground: Color.Orange3, decoration: Decoration.Bold);
                receiverPrompt.SearchHighlightStyle = new Style(foreground: Color.Orange1, decoration: Decoration.Underline);

                var receicer = AnsiConsole.Prompt(receiverPrompt);

                var amountPrompt = new TextPrompt<decimal>("[orange1 bold]Enter amount to transfer:[/]")
                    .Validate(input => input > 0, "[red][bold]Error:[/] Amount must be a positive number.[/]")
                    .ClearOnFinish();
                var amountToTransfer = AnsiConsole.Prompt(amountPrompt);

                var feePrompt = new TextPrompt<decimal>("[orange1 bold]Enter transaction fee:[/]")
                    .DefaultValue(1.0m)
                    .ShowDefaultValue()
                    .Validate(input => input > 0, "[red][bold]Error:[/] Fee must be a positive number.[/]")
                    .ClearOnFinish();
                var transactionFee = AnsiConsole.Prompt(feePrompt);

                //TODO:Add Confirmation (WITH BALANCE)

                var newTransaction = TransactionService.CreateTransaction(myWallet.Address, receicer.Item2, amountToTransfer, transactionFee, myWallet);
                await blockChainService.AddTransactionAsync(newTransaction);
                UI.SuccessPrint("Transaction added.");
            } 
            catch (InvalidOperationException ex)
            {
                UI.ErrorPrint(ex.Message);
            }
            break;
        case 8:
            var pendingTransactions = blockChainService.GetPendingTransactionsSnapshot();
            if (pendingTransactions.Count == 0)
            {
                UI.InfoPrint("No pending transactions.");
            }
            else
            {
                BlockChainDisplayService.DisplayTransactions(pendingTransactions);
            }
            break;
        case 9:
            var difficultyPrompt = new TextPrompt<int>("[orange1 bold]Enter max difficulty:[/]")
                    .DefaultValue(7)
                    .ShowDefaultValue()
                    .Validate(input => input > 0, "[red][bold]Error:[/] Difficulty must be a positive integer.[/]")
                    .ClearOnFinish();
            var maxDifficulty = AnsiConsole.Prompt(difficultyPrompt);

            try
            {
                await miningService.TestMiningEfficiencyAsync(maxDifficulty);
            }
            catch (Exception ex)
            {
                AnsiConsole.Clear();
                UI.ErrorPrint(ex.Message);
            }

            break;
        case 10:
            try
            {
                var syncResult = await blockChainService.ExchangeChainAsync(new NetworkEndpoint("127.0.0.1", blockChainService.NodeSendPort));
                if (syncResult.Accepted)
                {
                    UI.SuccessPrint("A longer valid blockchain was accepted.");
                }
                else
                {
                    UI.InfoPrint(syncResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                UI.ErrorPrint(ex.Message, "Synchronization failed");
            }
            break;
        case 11:
            UI.CustomPrint(blockChainService.GetTotalSupply() + " coins", "Total blockchain supply", Color.Gold1);
            break;
        case 12:
            var transactionsToSend = blockChainService.GetPendingTransactionsSnapshot();
            if (transactionsToSend.Count == 0)
            {
                UI.InfoPrint("There are no pending transactions to send.");
                break;
            }

            var transactionPrompt = new SelectionPrompt<Transaction>()
                .Title("[bold orange1]Select a transaction to send:[/]")
                .MoreChoicesText("[grey dim](Move up and down to reveal more options)[/]")
                .EnableSearch()
                .SearchPlaceholderText("[grey dim](Type to search...)[/]")
                .UseConverter(transaction => $"{transaction.Id} | {transaction.Amount} (+ {transaction.Fee} fee)")
                .AddChoices(transactionsToSend);
            transactionPrompt.HighlightStyle = new Style(foreground: Color.Orange3, decoration: Decoration.Bold);
            transactionPrompt.SearchHighlightStyle = new Style(foreground: Color.Orange1, decoration: Decoration.Underline);

            var transactionToSend = AnsiConsole.Prompt(transactionPrompt);

            try
            {
                var response = await BlockNetworkService.SendAndReceiveAsync(
                    new NetworkEndpoint("127.0.0.1", blockChainService.NodeSendPort),
                    new NetworkMessage { Type = NetworkMessageType.Transaction, Transaction = transactionToSend });
                if (response?.Type == NetworkMessageType.Rejected)
                {
                    UI.ErrorPrint(response.Error ?? "The peer rejected the transaction.", "Transaction rejected");
                }
                else
                {
                    UI.SuccessPrint($"Transaction {transactionToSend.Id} was sent to the peer.");
                }
            }
            catch (Exception ex)
            {
                UI.ErrorPrint(ex.Message, "Error sending transaction");
            }
            break;
        case 13:
            try
            {
                var lastBlock = blockChainService.GetChainSnapshot().Last();
                var response = await BlockNetworkService.SendAndReceiveAsync(
                    new NetworkEndpoint("127.0.0.1", blockChainService.NodeSendPort),
                    new NetworkMessage { Type = NetworkMessageType.Block, Block = lastBlock });
                if (response?.Type == NetworkMessageType.Rejected)
                {
                    UI.ErrorPrint(response.Error ?? "The peer rejected the block.", "Block rejected");
                }
                else
                {
                    UI.SuccessPrint("Last block sent to network.");
                }
            }
            catch (Exception ex)
            {
                UI.ErrorPrint(ex.Message, "Error sending block");
            }
            break;
        case 14:
            BlockChainDisplayService.DisplayBlockchainSpecs(blockChainService);
            break;
        case 15:
            var listenPortPrompt = new TextPrompt<int>("[orange1 bold]Enter listen port:[/]")
                    .DefaultValue(blockChainService.NodeListenPort)
                    .ShowDefaultValue()
                    .Validate(input => input > 0, "[red][bold]Error:[/] Port must be a positive number.[/]")
                    .ClearOnFinish();
            var listenPortInput = AnsiConsole.Prompt(listenPortPrompt);

            var sendPortPrompt = new TextPrompt<int>("[orange1 bold]Enter send port:[/]")
                    .DefaultValue(blockChainService.NodeSendPort)
                    .ShowDefaultValue()
                    .Validate(input => input > 0, "[red][bold]Error:[/] Port must be a positive number.[/]")
                    .ClearOnFinish();
            var sendPortInput = AnsiConsole.Prompt(sendPortPrompt);

            //TODO:Add Confirmation

            try
            {
                await blockChainService.RestartBackgroundSyncAsync(listenPortInput, sendPortInput);
                UI.SuccessPrint($"Listener restarted on port {listenPortInput}. Peer port: {sendPortInput}.");
                blockChainService.ChainFilePath = $"blockchain-{nodeListenPort}.json";
            }
            catch (Exception ex)
            {
                UI.ErrorPrint(ex.Message, "Failed to restart listener");
            }
            break;
        case 16:
            blockChainService.SaveChain();

            await blockChainService.DisposeAsync();

            return;
        default:
            UI.ErrorPrint("Invalid option. Please try again.");
            break;
    }

    await UI.AwaitingInput();
}
