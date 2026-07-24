using BlockChain.Models;
using BlockChain.Services;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

static bool IsValidHashPrefix(string input)
{
    if (string.IsNullOrEmpty(input)) return false;
    return Regex.IsMatch(input, @"^[0-9a-fA-F]{1,64}$");
}

var hashingService = new HashingService();
var miningService = new MiningService(hashingService);
var transactionService = new TransactionService();
var blockChainService = new BlockChainService(hashingService, miningService, transactionService);

List<Transaction> pendingTransactions = new List<Transaction>();
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
    Console.WriteLine("9. Exit");
    Console.Write("Enter your choice: ");
    var selectedOption = Console.ReadLine();

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
            else Console.WriteLine("Difficulty cannot be less than 1.");
            break;
        case "6":
            pendingTransactions.Add(new Transaction("Alice", "Bob", 100));

            //Console.Write("Enter transaction data: ");
            //var transactionData = Console.ReadLine();
            //if (!string.IsNullOrEmpty(transactionData))
            //{
            //    var transaction = new Transaction(transactionData);
            //    pendingTransactions.Add(transaction);
            //    Console.WriteLine("Transaction added to pending transactions.");
            //}
            //else
            //{
            //    Console.WriteLine("Transaction data cannot be empty.");
            //}
            break;
        case "7":
            if (pendingTransactions.Count == 0) Console.WriteLine("No pending transactions.");
            else BlockChainDisplayService.DisplayTransactions(pendingTransactions);
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

                miningService.TestMiningEffieciency(maxDifficulty);
            }
            else
            {
                Console.WriteLine("Invalid number. Please enter a valid integer.");
            }
            break;
        case "9":
            return;
        default:
            Console.WriteLine("Invalid option. Please try again.");
            break;
    }
    Console.Write("Press any key to continue...");
    Console.ReadKey();
    Console.Clear();
}