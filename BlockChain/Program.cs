using BlockChain.Models;
using BlockChain.Services;
using Spectre.Console;
using System.Net.NetworkInformation;
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
    await ContinueInput();
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
        ( 12, "Exit" )
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
                    10);

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
            blockChainService.SaveChain();
            return;
        default:
            ErrorPrint("Invalid option. Please try again.");
            break;
    }

    await ContinueInput();
}

static async Task ContinueInput(string message = "Press any key to continue", Style? style = null, int delayMs = 800)
{
    style ??= new Style(foreground: Color.Gray, decoration: Decoration.Bold);
    short dotCounter = 1;
    var cts = new CancellationTokenSource();
    CancellationToken token = cts.Token;

    Console.Write(Environment.NewLine);
    Task dotTask = AnsiConsole.Live(new Markup(message, style))
        .StartAsync(async ctx =>
        {
            while (true)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    ctx.UpdateTarget(new Markup(message + new string('.', dotCounter), style));

                    dotCounter = (short)((dotCounter + 1) % 4);
                    await Task.Delay(delayMs, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });

    await Task.Run(() => {
        Console.ReadKey(intercept: true);
        cts.Cancel();
    });

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