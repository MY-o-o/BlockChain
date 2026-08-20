using BlockChain.Models;

namespace BlockChain.Services
{
    public class BlockChainService
    {
        private readonly HashingService _hashingService;
        private readonly MiningService _miningService;
        private readonly TransactionService _transactionService;
        public List<Block> Chain { get; set; }
        public int Difficulty { get; set; } = 4;
        private readonly int _targetTimePerBlock = 2000; // Target time per block in milliseconds
        private readonly int _adjustmentInterval = 2; // Number of blocks after which to adjust difficulty
        private readonly decimal _rewardAmount = 50; // Reward amount for mining a block

        public BlockChainService(HashingService hashingService, MiningService miningService, TransactionService transactionService)
        {
            _hashingService = hashingService;
            _miningService = miningService;
            _transactionService = transactionService;
            Chain = new List<Block>();
            CreateGenesisBlock();
        }

        private void CreateGenesisBlock()
        {
            var genesisBlock = new Block(0, new List<Transaction>(), "Genesis Block", Difficulty);

            _miningService.MineBlock(genesisBlock, Difficulty, showProgress: false);
            Chain.Add(genesisBlock);
        }

        public void AddBlock(List<Transaction> pendingTransactions, string minerAddress, bool showProgress = true)
        {
            var currentBalances = new Dictionary<string, decimal>();
            foreach (var transaction in pendingTransactions)
            {
                var result = _transactionService.ValidateTransaction(transaction);
                if (!result.isValid)
                {
                    throw new InvalidOperationException($"Invalid transaction: {result.errorMessage}");
                }

                if (!currentBalances.ContainsKey(transaction.From))
                {
                    currentBalances[transaction.From] = GetWalletBalance(transaction.From);
                }

                if(currentBalances[transaction.From] < transaction.Amount)
                {
                    throw new InvalidOperationException($"Insufficient balance for transaction from {transaction.From} to {transaction.To}. Available balance: {currentBalances[transaction.From]}, required: {transaction.Amount}");
                }
                currentBalances[transaction.From] -= transaction.Amount;
            }

            var transactionCopy = pendingTransactions.Select(t => (Transaction)t.Clone()).ToList();

            var rewardTransaction = new Transaction("Coinbase", minerAddress, _rewardAmount);
            transactionCopy.Add(rewardTransaction);

            var lastBlock = Chain.Last();
            var newBlock = new Block(lastBlock.Index + 1, transactionCopy, lastBlock.Hash, Difficulty);

            _miningService.MineBlock(newBlock, Difficulty, showProgress);
            Chain.Add(newBlock);

            if (newBlock.Index % _adjustmentInterval == 0)
            {
                AdjustDifficulty();
            }
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

        public bool IsValid()
        {
            for (int i = 1; i < Chain.Count; i++)
            {
                var currentBlock = Chain[i];
                var previousBlock = Chain[i - 1];
                if (currentBlock.Hash != _hashingService.ComputeHash(currentBlock)) return false;
                if (currentBlock.PrevHash != previousBlock.Hash) return false;
                if (!currentBlock.Hash.StartsWith(new string('0', currentBlock.Difficulty))) return false;
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
                        balance -= transaction.Amount;
                    }
                    if (transaction.To == walletAddress)
                    {
                        balance += transaction.Amount;
                    }
                }
            }
            return balance;
        }

        public Dictionary<string, decimal> GetPendingBalances(List<Transaction> pendingTransactions)
        {
            var pendingBalances = new Dictionary<string, decimal>();
            foreach (var transaction in pendingTransactions)
            {
                pendingBalances[transaction.From] -= transaction.Amount;
                pendingBalances[transaction.To] += transaction.Amount;
            }
            return pendingBalances;
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
    }
}
