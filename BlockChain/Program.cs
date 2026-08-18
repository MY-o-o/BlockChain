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
for (int blockNumber = 1; blockNumber < 5; blockNumber++)
{
    blockChainService.Difficulty = 4;
    blockChainService.AddBlock(
        [new Transaction($"Wallet-{blockNumber}", $"Wallet-{blockNumber + 1}", blockNumber * 10)],
        showProgress: false);
}
blockChainService.Difficulty = 4;

var aliceWallet = walletService.CreateWallet("Alice");
var bobWallet = walletService.CreateWallet("Bob");

// end of tmp code

List<Transaction> pendingTransactions = [];
while (true)
{
    Console.WriteLine("Block Management Menu:");
    Console.WriteLine("1. Add a new block");
    Console.WriteLine("2. Display the blockchain");
    Console.WriteLine("3. Validate the blockchain");
    Console.WriteLine("4. Change difficulty ++");
    Console.WriteLine("5. Change difficulty --");
    Console.WriteLine("6. Add a new transaction");
    Console.WriteLine("7. Display pending transactions");
    Console.WriteLine("8. Test mining efficiency");
    Console.WriteLine("9. Hack the blockchain");
    Console.WriteLine("10. Exit");
    Console.Write("Enter your choice: ");
    string? selectedOption = Console.ReadLine();

    switch (selectedOption)
    {
        case "1":
            blockChainService.AddBlock(pendingTransactions);
            pendingTransactions.Clear();

            Console.WriteLine("Block added!");
            break;
        case "2":
            BlockChainDisplayService.DisplayBlockChain(blockChainService.Chain);
            break;
        case "3":
            BlockChainDisplayService.DisplayValidationResult(blockChainService.IsValid());
            break;
        case "4":
            blockChainService.Difficulty++;
            Console.WriteLine($"Difficulty increased to {blockChainService.Difficulty}");
            break;
        case "5":
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
        case "6":
            var newTransaction = transactionService.CreateTransaction(aliceWallet.Address, bobWallet.Address, 100, aliceWallet);
            pendingTransactions.Add(newTransaction);
            Console.WriteLine("Transaction added.");
            break;
        case "7":
            if (pendingTransactions.Count == 0)
            {
                Console.WriteLine("No pending transactions.");
            }
            else
            {
                BlockChainDisplayService.DisplayTransactions(pendingTransactions);
            }
            break;
        case "8":
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
        case "9":
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
                    forgedAmount);

                await tamperingService.HackChain(
                    blockChainService,
                    blockIndex,
                    forgedTransaction);

                BlockChainDisplayService.DisplayValidationResult(blockChainService.IsValid());
            }
            catch (ArgumentException exception)
            {
                Console.WriteLine(exception.Message);
            }
            break;
        case "10":
            return;
        default:
            Console.WriteLine("Invalid option. Please try again.");
            break;
    }

    Console.Write("Press any key to continue...");
    Console.ReadKey();
    Console.Clear();
}