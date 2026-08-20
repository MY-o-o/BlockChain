using BlockChain.Models;

namespace BlockChain.Services
{
    public class BlockChainService
    {
        private readonly HashingService _hashingService;
        private readonly MiningService _miningService;
        private readonly TransactionService _transactionService;
        public List<Block> Chain { get; set; } = [];
        public List<Transaction> PendingTransactions { get; set; } = [];
        public int Difficulty { get; set; } = 5;
        private readonly int _targetTimePerBlock = 2000; // Target time per block in milliseconds
        private readonly int _adjustmentInterval = 2; // Number of blocks after which to adjust difficulty
        private readonly decimal _rewardAmount = 50; // Reward amount for mining a block
        private readonly int _halvingInterval = 3; // Number of blocks after which the reward is halved

        public BlockChainService(HashingService hashingService, MiningService miningService, TransactionService transactionService)
        {
            _hashingService = hashingService;
            _miningService = miningService;
            _transactionService = transactionService;
            CreateGenesisBlock();
        }

        private void CreateGenesisBlock()
        {
            var genesisBlock = new Block(0, new List<Transaction>(), "Genesis Block", Difficulty);

            _miningService.MineBlock(genesisBlock, Difficulty, showProgress: false);
            Chain.Add(genesisBlock);
        }

        public void MinePendingTransactions(string minerAddress, bool showProgress = true)
        {
            var transactionsCopy = PendingTransactions.Select(t => (Transaction)t.Clone()).ToList();
            var rewardTransaction = new Transaction("Coinbase", minerAddress, GetCurrentReward());
            transactionsCopy.Insert(0, rewardTransaction);

            var lastBlock = Chain.Last();
            var newBlock = new Block(lastBlock.Index + 1, transactionsCopy, lastBlock.Hash, Difficulty);

            //TODO: Insert a varible newBlock as a ref newBlock
            _miningService.MineBlock(newBlock, Difficulty, showProgress);
            Chain.Add(newBlock);
            PendingTransactions.Clear();

            if (newBlock.Index % _adjustmentInterval == 0)
            {
                AdjustDifficulty();
            }
        }

        public void AddTransaction(Transaction newTransaction)
        {
            var (isValid, errorMessage) = _transactionService.ValidateTransaction(newTransaction);
            if (!isValid)
            {
                throw new InvalidOperationException($"Invalid transaction: {errorMessage}");
            }

            var senderMinedBalance = GetWalletBalance(newTransaction.From);
            var pendingBalance = PendingTransactions.Sum( t => t.From == newTransaction.From ? t.Amount : 0);
            var senderBalance = senderMinedBalance - pendingBalance;
            if (senderBalance < newTransaction.Amount)
            {
                throw new InvalidOperationException($"Insufficient balance for transaction from {newTransaction.From} to {newTransaction.To}. Available balance: {senderBalance}, required: {newTransaction.Amount}");
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

        public bool IsValid()
        {
            for (int i = 1; i < Chain.Count; i++)
            {
                var currentBlock = Chain[i];
                var previousBlock = Chain[i - 1];

                int pastHalvings = currentBlock.Index / _halvingInterval; 
                decimal expectedReward = _rewardAmount / (decimal)Math.Pow(2, pastHalvings);

                if (currentBlock.Hash != _hashingService.ComputeHash(currentBlock)) return false;
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

        public decimal GetCurrentReward()
        {
            int halvingCount = Chain.Count / _halvingInterval;
            return _rewardAmount / (decimal)Math.Pow(2, halvingCount);
        }
    }
}
