using BlockChain.Models;
using BlockChain.Services;
using Spectre.Console;
using System.Text;

//TODO: Implement DI

Console.OutputEncoding = Encoding.UTF8;

var walletService = new WalletService();
var miningService = new MiningService();
var transactionService = new TransactionService(walletService);
var tamperingService = new BlockchainTamperingService(miningService);
var blockChainService = new BlockChainService(miningService, transactionService);


// tmp code

var aliceWallet = walletService.CreateWallet("Alice");
var bobWallet = walletService.CreateWallet("Bob");

// end of tmp code

try
{
    blockChainService.LoadChain();
}
catch (Exception ex)
{
    ErrorPrint(ex.Message, "Error loading blockchain");
    WarningPrint("Starting with a new blockchain.");
    await AwaitingInput();
}

while (true)
{
    var menuOptions = new (short, string)[]
    {
        ( 1, "Add a new block (Alice)" ),
        ( 2, "Display the blockchain" ),
        ( 3, "Validate the blockchain" ),
        ( 4, "Change difficulty ++" ),
        ( 5, "Change difficulty --" ),
        ( 6, "Display balance" ),
        ( 7, "Transfer coins from Alice to Bob" ),
        ( 8, "Display pending transactions" ),
        ( 9, "Test mining efficiency" ),
        ( 10, "Tamper with a block" ),
        ( 11, "Display total blockchain supply" ),
        ( 12, "Wait for incoming block from network" ),
        ( 13, "Send last block to network" ),
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
                var result = await blockChainService.MinePendingTransactionsAsync(aliceWallet.Address);
                MiningService.DisplayMiningResult(result);
            }
            catch (Exception ex)
            {
                ErrorPrint(ex.Message);
            }
            break;
        case 2:
            BlockChainDisplayService.DisplayBlockChain(blockChainService.Chain);
            break;
        case 3:
            BlockChainDisplayService.DisplayValidationResult(blockChainService.IsValidChain(blockChainService.Chain));
            break;
        case 4:
            //TODO: Implement a more sophisticated difficulty adjustment mechanism based on mining time and other factors
            blockChainService.Difficulty++;
            InfoPrint($"Difficulty increased to {blockChainService.Difficulty}");
            break;
        case 5:
            if (blockChainService.Difficulty > 1)
            {
                blockChainService.Difficulty--;
                InfoPrint($"Difficulty decreased to {blockChainService.Difficulty}");
            }
            else ErrorPrint("Difficulty cannot be less than 1.");
            break;
        case 6:
            CustomPrint(blockChainService.GetWalletBalance(aliceWallet.Address) + " coins", "Your balance", Color.Gold1);
            break;
        case 7:
            try
            {
                var amountPrompt = new TextPrompt<decimal>("[orange1 bold]Enter amount to transfer from Alice to Bob:[/]")
                    .Validate(input => input > 0, "[red][bold]Error:[/] Amount must be a positive number.[/]")
                    .ClearOnFinish();
                var amountToTransfer = AnsiConsole.Prompt(amountPrompt);

                var feePrompt = new TextPrompt<decimal>("[orange1 bold]Enter transaction fee:[/]")
                    .DefaultValue(1.0m)
                    .ShowDefaultValue()
                    .Validate(input => input > 0, "[red][bold]Error:[/] Fee must be a positive number.[/]")
                    .ClearOnFinish();
                var transactionFee = AnsiConsole.Prompt(feePrompt);

                //TODO:Add Confirmation

                var newTransaction = transactionService.CreateTransaction(aliceWallet.Address, bobWallet.Address, amountToTransfer, transactionFee, aliceWallet);
                blockChainService.AddTransaction(newTransaction);
                SuccessPrint("Transaction added.");
            } 
            catch (InvalidOperationException ex)
            {
                ErrorPrint(ex.Message);
            }
            break;
        case 8:
            if (blockChainService.PendingTransactions.Count == 0)
            {
                InfoPrint("No pending transactions.");
            }
            else
            {
                BlockChainDisplayService.DisplayTransactions(blockChainService.PendingTransactions);
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
                ErrorPrint(ex.Message);
            }

            break;
        case 10:
            //TODO: Implement a better UI/UX input and upgrade the tampering service
            Console.Write($"Enter block index to tamper with (0-{blockChainService.Chain.Count - 1}): ");
            if (!int.TryParse(Console.ReadLine(), out int blockIndex))
            {
                ErrorPrint("Invalid block index.");
                break;
            }

            Console.Write("Forged sender: ");
            string forgedSender = Console.ReadLine() ?? string.Empty;
            Console.Write("Forged recipient: ");
            string forgedRecipient = Console.ReadLine() ?? string.Empty;
            Console.Write("Forged amount: ");

            if (!decimal.TryParse(Console.ReadLine(), out decimal forgedAmount))
            {
                ErrorPrint("Invalid amount.");
                break;
            }

            try
            {
                var forgedTransaction = new Transaction(
                    forgedSender,
                    forgedRecipient,
                    forgedAmount,
                    0);

                await tamperingService.HackChain(
                    blockChainService,
                    blockIndex,
                    forgedTransaction);

                BlockChainDisplayService.DisplayValidationResult(blockChainService.IsValidChain(blockChainService.Chain));
            }
            catch (ArgumentException exception)
            {
                ErrorPrint(exception.Message);
            }
            break;
        case 11:
            CustomPrint(blockChainService.GetTotalSupply() + " coins", "Total blockchain supply", Color.Gold1);
            break;
        case 12:
            try
            {
                //var cancelKey = ConsoleKey.Escape;
                var cts = new CancellationTokenSource();
                var token = cts.Token;
                var awaitingTask = AwaitingInput(
                    $"Waiting for incoming block from network on port {blockChainService.NodeListenPort}",
                    //$"\n[dim gray]Press [bold]{cancelKey}[/] to cancel[/]",
                    new Style(Color.SteelBlue1_1), isInPanel: true, isCancelable: false, cts: cts);

                //var listenerTask = Task.Run( async () =>
                //{
                //    if (Console.IsInputRedirected) return;
                //    while (cts.IsCancellationRequested)
                //    {
                //        if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == cancelKey)
                //        {
                //            cts.Cancel();
                //            return;
                //        }

                //        await Task.Delay(100, token);
                //    }
                //});

                var receivedBlock = await BlockNetworkService.ReceiveBlockAsync(blockChainService.NodeListenPort, token);
                var (accepted, errorMessage) = blockChainService.TryAddBlockFromNet(receivedBlock);

                cts.Cancel();
                //await listenerTask;
                await awaitingTask;

                AnsiConsole.Clear();
                if (accepted)
                {
                    SuccessPrint("Received block accepted and added to the blockchain.");
                }
                else
                {
                    ErrorPrint(errorMessage, "Received block rejected");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.Clear();
                ErrorPrint(ex.Message, "Error receiving block");
            }
            break;
        case 13:
            try
            {
                var lastBlock = blockChainService.Chain.Last();
                await BlockNetworkService.SendBlockAsync(lastBlock, "127.0.0.1", blockChainService.NodeListenPort); //fix ports
                SuccessPrint("Last block sent to network.");
            }
            catch (Exception ex)
            {
                ErrorPrint(ex.Message, "Error sending block");
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
            var listenPort = AnsiConsole.Prompt(listenPortPrompt);

            var sendPortPrompt = new TextPrompt<int>("[orange1 bold]Enter send port:[/]")
                    .DefaultValue(blockChainService.NodeSendPort)
                    .ShowDefaultValue()
                    .Validate(input => input > 0, "[red][bold]Error:[/] Port must be a positive number.[/]")
                    .ClearOnFinish();
            var sendPort = AnsiConsole.Prompt(sendPortPrompt);

            //TODO:Add Confirmation

            SuccessPrint("Ports were changed");
            break;
        case 16:
            blockChainService.SaveChain();
            return;
        default:
            ErrorPrint("Invalid option. Please try again.");
            break;
    }

    await AwaitingInput();
}

static async Task AwaitingInput(
    string message = "Press any key to continue", 
    Style? style = null, 
    int delayMs = 800, 
    bool isInPanel = false, 
    bool isCancelable = true,
    CancellationTokenSource? cts = null)
{
    style ??= new Style(foreground: Color.Gray, decoration: Decoration.Bold);
    cts ??= new CancellationTokenSource();
    CancellationToken token = cts.Token;
    short dotCounter = 1;

    Console.Write(Environment.NewLine);

    var messageMarkup = new Markup(message, style);
    var panel = new Panel(messageMarkup)
        .BorderColor(style.Value.Foreground)
        .Border(BoxBorder.Rounded)
        .Padding(2, 0);

    Task dotTask = AnsiConsole.Live(isInPanel ? panel : messageMarkup)
        .StartAsync(async ctx =>
        {
            while (true)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    var newMessageMarkup = new Markup(message + new string('.', dotCounter) + new string(' ', 3 - dotCounter), style);
                    var newPanel = new Panel(newMessageMarkup)
                        .BorderColor(style.Value.Foreground)
                        .Border(BoxBorder.Rounded)
                        .Padding(2, 0);
                    ctx.UpdateTarget(isInPanel ? newPanel : newMessageMarkup);

                    dotCounter = (short)((dotCounter + 1) % 4);
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

    if (isCancelable)
    {
        await Task.Run(() => {
            Console.ReadKey(intercept: true);
            cts.Cancel();
        });
    }

    try
    {
        await dotTask;
    }
    catch (Exception ex)
    {
        ErrorPrint(ex.Message);
    }

    AnsiConsole.Clear();
}  

static void CustomPrint(string message, string caption = "", Color? color = null)
{
    color ??= Color.Gray;
    
    int horizontalAlignment = 0;
    if (caption.Length > message.Length)
    {
        horizontalAlignment = (caption.Length - message.Length) / 2;
    }

    var panel = new Panel($"[bold {color.Value}]{message}[/]")
        .Header($"[{color.Value}]{caption}[/]")
        .BorderColor(color ?? Color.Gray)
        .Border(BoxBorder.Rounded)
        .Padding(2 + horizontalAlignment, 0);
    AnsiConsole.Write(panel);
}
static void ErrorPrint(string message, string caption = "Error") => CustomPrint(message, caption, Color.Red);
static void SuccessPrint(string message, string caption = "Success") => CustomPrint(message, caption, Color.Green);
static void WarningPrint(string message, string caption = "Warning") => CustomPrint(message, caption, Color.Yellow);
static void InfoPrint(string message, string caption = "Info") => CustomPrint(message, caption, Color.SteelBlue1_1);