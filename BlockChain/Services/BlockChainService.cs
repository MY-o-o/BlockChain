using BlockChain.Models;
using System.Text.Json;

namespace BlockChain.Services
{
    public class BlockChainService
    {
        private readonly MiningService _miningService;
        private readonly TransactionService _transactionService;
        public List<Block> Chain { get; set; } = [];
        public List<Transaction> PendingTransactions { get; set; } = [];
        public int Difficulty { get; set; } = 5;
        public int NodeListenPort { get; set; } = 533;
        public int NodeSendPort { get; set; } = 534;
        private const int _targetTimePerBlock = 2000; // Target time per block in milliseconds
        private const int _adjustmentInterval = 2; // Number of blocks after which to adjust difficulty
        private const decimal _rewardAmount = 50; // Reward amount for mining a block
        private const int _maxTransactionAmountPerBlock = 3; // Maximum number of transactions per block
        private const int _halvingInterval = 10; // Number of blocks after which the reward is halved
        private const string _chainFilePath = "blockchain.json";

        public BlockChainService(MiningService miningService, TransactionService transactionService)
        {
            _miningService = miningService;
            _transactionService = transactionService;
            CreateGenesisBlock();
        }

        private void CreateGenesisBlock()
        {
            var genesisBlock = new Block(0, [], "Genesis Block", Difficulty)
            {
                TimeStamp = DateTime.UnixEpoch
            };

            _miningService.MineBlock(genesisBlock, Difficulty, showProgress: false, cancelKey: ConsoleKey.None);
            Chain.Add(genesisBlock);
        }

        public async Task<MiningResult> MinePendingTransactionsAsync(
            string minerAddress,
            bool showProgress = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(minerAddress);

            var transactionsCopy = PendingTransactions.OrderByDescending(t => t.Fee).Take(_maxTransactionAmountPerBlock).ToList();
            var totalFees = transactionsCopy.Sum(t => t.Fee);
            var rewardTransaction = new Transaction("Coinbase", minerAddress, GetCurrentReward() + totalFees, 0);
            transactionsCopy.Insert(0, rewardTransaction);

            var lastBlock = Chain.Last();
            var newBlock = new Block(lastBlock.Index + 1, transactionsCopy, lastBlock.Hash, Difficulty);

            var result = await _miningService.MineBlockAsync(
                newBlock,
                Difficulty,
                showProgress,
                cancellationToken: cancellationToken);
            Chain.Add(newBlock);

            foreach (var transaction in transactionsCopy)
            {
                PendingTransactions.Remove(transaction);
            }

            if (newBlock.Index % _adjustmentInterval == 0)
            {
                AdjustDifficulty();
            }

            return result;
        }

        public void AddTransaction(Transaction newTransaction)
        {
            var (isValid, errorMessage) = _transactionService.ValidateTransaction(newTransaction);
            if (!isValid)
            {
                throw new InvalidOperationException($"Invalid transaction: {errorMessage}");
            }

            var senderBalance = GetWalletBalance(newTransaction.From);
            if (senderBalance < newTransaction.Amount + newTransaction.Fee)
            {
                throw new InvalidOperationException($"Insufficient balance for transaction from\n{newTransaction.From}\nto\n{newTransaction.To}\nAvailable balance: {senderBalance}; required: {newTransaction.Amount} + {newTransaction.Fee}(fee)");
            }

            PendingTransactions.Add(newTransaction);
        }

        private void AdjustDifficulty()
        {
            var lastBlock = Chain.Last();
            var previousAdjustmentBlock = Chain[Chain.Count - _adjustmentInterval];
            var actualTimeTaken = (lastBlock.TimeStamp - previousAdjustmentBlock.TimeStamp).TotalMilliseconds;
            var actualTimeTakenPerBlock = actualTimeTaken / _adjustmentInterval;

            if (actualTimeTakenPerBlock < _targetTimePerBlock)
            {
                Difficulty++;
            }
            else if (actualTimeTakenPerBlock > _targetTimePerBlock && Difficulty > 1)
            {
                Difficulty--;
            }
        }

        public bool IsValidChain(List<Block> chain)
        {
            for (int i = 1; i < chain.Count; i++)
            {
                var currentBlock = chain[i];
                var previousBlock = chain[i - 1];

                int pastHalvings = currentBlock.Index / _halvingInterval;
                decimal expectedReward = _rewardAmount / (decimal)Math.Pow(2, pastHalvings);
                expectedReward += chain[i].Transactions.Sum(t => t.From == "Coinbase" ? 0 : t.Fee);

                if (currentBlock.Hash != HashingService.ComputeHash(currentBlock)) return false;
                if (currentBlock.PrevHash != previousBlock.Hash) return false;
                if (!currentBlock.Hash.StartsWith(new string('0', currentBlock.Difficulty))) return false;

                if (currentBlock.Transactions.First().From != "Coinbase") return false;
                if (currentBlock.Transactions.First().Amount != expectedReward) return false;
            }
            return true;
        }

        public decimal GetWalletBalance(string walletAddress)
        {
            decimal balance = 0;
            foreach (var block in Chain)
            {
                foreach (var transaction in block.Transactions)
                {
                    if (transaction.From == walletAddress)
                    {
                        balance -= transaction.Amount + transaction.Fee;
                    }
                    if (transaction.To == walletAddress)
                    {
                        balance += transaction.Amount;
                    }
                }
            }

            balance -= PendingTransactions.Sum(t => t.From == walletAddress ? t.Amount + t.Fee : 0);

            return balance;
        }

        public decimal GetTotalSupply()
        {
            decimal totalSupply = 0;

            foreach (var block in Chain)
            {
                foreach (var transaction in block.Transactions)
                {
                    if (transaction.From == "Coinbase")
                    {
                        totalSupply += transaction.Amount;
                    }
                }
            }

            return totalSupply;
        }

        public decimal GetCurrentReward()
        {
            int halvingCount = Chain.Count / _halvingInterval;
            return _rewardAmount / (decimal)Math.Pow(2, halvingCount);
        }

        private readonly JsonSerializerOptions jsonOption = new() { WriteIndented = true };
        public void SaveChain()
        {
            var json = JsonSerializer.Serialize(Chain, jsonOption);

            File.WriteAllText(_chainFilePath, json);
        }

        public void LoadChain()
        {
            if (!File.Exists(_chainFilePath))
            {
                throw new InvalidOperationException("No existing blockchain found.");
            }

            var json = File.ReadAllText(_chainFilePath);
            var loadedChain = JsonSerializer.Deserialize<List<Block>>(json);

            if (loadedChain == null || !IsValidChain(loadedChain))
            {
                throw new InvalidOperationException("Failed to load blockchain.");
            }

            Chain = loadedChain;
            Difficulty = Chain.Last().Difficulty;
        }

        public (bool accepted, string errorMessage) TryAddBlockFromNet(Block block)
        {
            var latestBlock = Chain.Last();

            //1. Check if the new block's index is one greater than the latest block's index
            if (block.Index != latestBlock.Index + 1)
            {
                return (false, $"Invalid block index. Expected index: {latestBlock.Index + 1}, Received index: {block.Index}");
            }
            //2. Check if the new block's previous hash matches the latest block's hash
            if (block.PrevHash != latestBlock.Hash)
            {
                return (false, "Invalid previous hash.");
            }
            //3. Validate the block's hash
            if (block.Hash != HashingService.ComputeHash(block))
            {
                return (false, "Invalid block hash.");
            }
            //4. Difficulty check
            //TODO: Difficulty check should be improved
            if (!block.Hash.StartsWith(new string('0', block.Difficulty)))
            {
                return (false, $"Invalid difficulty. Expected: {new string('0', block.Difficulty)}, Received: {block.Hash[..block.Difficulty]}");
            }
            //5. Already existing block check
            if (Chain.Any(b => b.Hash == block.Hash))
            {
                return (false, $"Block with hash \"{block.Hash}\" already exists in the chain.");
            }
            //6. Validate transactions in the block
            foreach (var transaction in block.Transactions)
            {
                var (isValid, errorMessage) = _transactionService.ValidateTransaction(transaction);
                if (!isValid)
                {
                    return (false, $"Invalid transaction in block: {errorMessage}");
                }
            }
            //7. Validate the reward transaction
            if (block.Transactions.FindAll(t => t.From == "Coinbase").Count != 1)
            {
                return (false, "Block must contain exactly one reward transaction.");
            }

            Chain.Add(block);
            return (true, string.Empty);
        }
    }
}
