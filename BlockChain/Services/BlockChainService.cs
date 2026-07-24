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

        public void AddBlock(List<Transaction> transactions, bool showProgress = true)
        {
            foreach (var transaction in transactions)
            {
                var result = _transactionService.ValidateTransaction(transaction);
                if (!result.isValid)
                {
                    throw new InvalidOperationException($"Invalid transaction: {result.errorMessage}");
                }
            }

            var transactionCopy = transactions.Select(t => (Transaction)t.Clone()).ToList();

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
    }
}
