using BlockChain.Models;
using BlockChain.Services;
using Spectre.Console;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var hashingService = new HashingService();
var miningService = new MiningService(hashingService);
var walletService = new WalletService();
var transactionService = new TransactionService(walletService);
var blockChainService = new BlockChainService(hashingService, miningService, transactionService);
var tamperingService = new BlockchainTamperingService(miningService);


// tmp code for the example of working blockchain

var aliceWallet = walletService.CreateWallet("Alice");
var bobWallet = walletService.CreateWallet("Bob");

// end of tmp code

try
{
    blockChainService.LoadChain();
}
catch (Exception ex)
{
    Console.WriteLine($"Error loading blockchain: {ex.Message}");
    Console.WriteLine("Starting with a new blockchain.\n");
    Console.Write("Press any key to continue...");
    Console.ReadKey();
    Console.Clear();
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
        ( 7, "Transfer 100 coins from Alice to Bob" ),
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
            blockChainService.MinePendingTransactions(aliceWallet.Address);
            Console.WriteLine("Block added!");
            break;
        case 2:
            BlockChainDisplayService.DisplayBlockChain(blockChainService.Chain);
            break;
        case 3:
            BlockChainDisplayService.DisplayValidationResult(blockChainService.IsValidChain(blockChainService.Chain));
            break;
        case 4:
            blockChainService.Difficulty++;
            Console.WriteLine($"Difficulty increased to {blockChainService.Difficulty}");
            break;
        case 5:
            if (blockChainService.Difficulty > 1)
            {
                blockChainService.Difficulty--;
                Console.WriteLine($"Difficulty decreased to {blockChainService.Difficulty}");
            }
            else
            {
                Console.WriteLine("Difficulty cannot be less than 1.");
            }
            break;
        case 6:
            Console.WriteLine($"Your balance: {blockChainService.GetWalletBalance(aliceWallet.Address)}");
            break;
        case 7:
            //TODO: Implement customisable transaction creation with user input for recipient(?), fee and amount
            try
            {
                Console.Write("Enter amount to transfer from Alice to Bob: ");
                var amountToTransfer = decimal.Parse(Console.ReadLine(), System.Globalization.CultureInfo.InvariantCulture);
                Console.Write("Enter transaction fee: ");
                var transactionFee = decimal.Parse(Console.ReadLine(), System.Globalization.CultureInfo.InvariantCulture);

                var newTransaction = transactionService.CreateTransaction(aliceWallet.Address, bobWallet.Address, amountToTransfer, transactionFee, aliceWallet);
                blockChainService.AddTransaction(newTransaction);
                Console.WriteLine("Transaction added.");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            break;
        case 8:
            if (blockChainService.PendingTransactions.Count == 0)
            {
                Console.WriteLine("No pending transactions.");
            }
            else
            {
                BlockChainDisplayService.DisplayTransactions(blockChainService.PendingTransactions);
            }
            break;
        case 9:
            Console.Write("Enter the max difficulty: ");
            if (short.TryParse(Console.ReadLine(), out short maxDifficulty))
            {
                if (maxDifficulty <= 0)
                {
                    Console.WriteLine("Max difficulty must be greater than zero.");
                    break;
                }

                miningService.TestMiningEfficiency(maxDifficulty);
            }
            else
            {
                Console.WriteLine("Invalid number. Please enter a valid integer.");
            }
            break;
        case 10:
            Console.Write($"Enter block index to tamper with (0-{blockChainService.Chain.Count - 1}): ");
            if (!int.TryParse(Console.ReadLine(), out int blockIndex))
            {
                Console.WriteLine("Invalid block index.");
                break;
            }

            Console.Write("Forged sender: ");
            string forgedSender = Console.ReadLine() ?? string.Empty;
            Console.Write("Forged recipient: ");
            string forgedRecipient = Console.ReadLine() ?? string.Empty;
            Console.Write("Forged amount: ");

            if (!decimal.TryParse(Console.ReadLine(), out decimal forgedAmount))
            {
                Console.WriteLine("Invalid amount.");
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
                Console.WriteLine(exception.Message);
            }
            break;
        case 11:
            Console.WriteLine("Total blockchain supply: " + blockChainService.GetTotalSupply());
            break;
        case 12:
            blockChainService.SaveChain();
            return;
        default:
            Console.WriteLine("Invalid option. Please try again.");
            break;
    }

    Console.Write("Press any key to continue...");
    Console.ReadKey();
    Console.Clear();
}