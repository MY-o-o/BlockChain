using BlockChain.Models;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace BlockChain.Services;

public readonly record struct ChainAdoptionResult(bool Accepted, string ErrorMessage);

public sealed class BlockChainService : IAsyncDisposable
{
    private readonly MiningService _miningService;
    private readonly BlockNetworkService _networkService;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _miningLifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _listenerLifecycleGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenerTask;
    private CancellationTokenSource? _listenerCancellation;
    private Task<MiningResult>? _activeMiningTask;

    // Do not mutate these lists outside this service. They remain public only to
    // keep the existing display and tampering exercise compatible.
    public List<Block> Chain { get; private set; } = [];
    public List<Transaction> PendingTransactions { get; private set; } = [];
    public int Difficulty { get; private set; } = 5;
    public int NodeListenPort { get; set; } = 533;
    public int NodeSendPort { get; set; } = 534;
    public string ChainFilePath;

    private const int TargetTimePerBlock = 2000;
    private const int AdjustmentInterval = 2;
    private const decimal RewardAmount = 50;
    private const int MaxTransactionAmountPerBlock = 3;
    private const int HalvingInterval = 10;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BlockChainService(MiningService miningService, BlockNetworkService? networkService = null, string? chainFilePath = null)
    {
        _miningService = miningService;
        _networkService = networkService ?? new BlockNetworkService();
        ChainFilePath = string.IsNullOrWhiteSpace(chainFilePath) ? "blockchain.json" : chainFilePath;

        try
        {
            LoadChain();
        }
        catch (Exception ex)
        {
            UIUXService.WarningPrint($"{ex.Message} Starting with a new blockchain.");
            CreateGenesisBlock();
        }
    }

    /// <summary>Starts the permanent network listener, then exchanges chains with known peers.</summary>
    public async Task StartBackgroundSyncAsync(IEnumerable<NetworkEndpoint> peers, CancellationToken cancellationToken = default)
    {
        await StartListenerAsync(NodeListenPort, cancellationToken);

        foreach (var peer in peers.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ExchangeChainAsync(peer, cancellationToken);
            }
            catch (SocketException)
            {
                // A configured peer can be offline during bootstrap.
            }
        }
    }

    /// <summary>
    /// Stops the current TCP listener, binds the requested port, and then performs
    /// a chain exchange with the newly configured peer. The chain and mempool stay
    /// in memory; only the network endpoint changes.
    /// </summary>
    public async Task RestartBackgroundSyncAsync(int listenPort, int sendPort, CancellationToken cancellationToken = default)
    {
        if (listenPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(listenPort));
        if (sendPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(sendPort));

        await _listenerLifecycleGate.WaitAsync(cancellationToken);
        try
        {
            int previousListenPort = NodeListenPort;
            int previousSendPort = NodeSendPort;
            await StopListenerUnsafeAsync();
            try
            {
                NodeListenPort = listenPort;
                NodeSendPort = sendPort;
                await StartListenerUnsafeAsync();
            }
            catch
            {
                // Do not leave a running node deaf because the requested port is occupied.
                NodeListenPort = previousListenPort;
                NodeSendPort = previousSendPort;
                await StartListenerUnsafeAsync();
                throw;
            }
        }
        finally
        {
            _listenerLifecycleGate.Release();
        }

        try
        {
            await ExchangeChainAsync(new NetworkEndpoint("127.0.0.1", NodeSendPort), cancellationToken);
        }
        catch (SocketException)
        {
            // The new peer may not be running yet; the node still listens normally.
        }
    }

    private async Task StartListenerAsync(int port, CancellationToken cancellationToken)
    {
        await _listenerLifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_listenerTask is not null) throw new InvalidOperationException("Background synchronization is already running.");
            NodeListenPort = port;
            await StartListenerUnsafeAsync();
        }
        finally
        {
            _listenerLifecycleGate.Release();
        }
    }

    private async Task StartListenerUnsafeAsync()
    {
        _listenerCancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _listenerTask = _networkService.ListenAsync(NodeListenPort, HandleNetworkMessageAsync, _listenerCancellation.Token);
        try
        {
            await Task.Yield(); // allow ListenAsync to bind before callers continue
            if (_listenerTask.IsFaulted) await _listenerTask;
        }
        catch
        {
            _listenerCancellation.Dispose();
            _listenerCancellation = null;
            _listenerTask = null;
            throw;
        }
    }

    private async Task StopListenerUnsafeAsync()
    {
        if (_listenerTask is null) return;

        _listenerCancellation?.Cancel();
        try { await _listenerTask; }
        catch (OperationCanceledException) { }
        finally
        {
            _listenerCancellation?.Dispose();
            _listenerCancellation = null;
            _listenerTask = null;
        }
    }

    /// <summary>
    /// Sends our valid chain to a peer. The peer adopts it only if it is longer;
    /// its response lets this node adopt a longer valid chain if it is longer.
    /// </summary>
    public async Task<ChainAdoptionResult> ExchangeChainAsync(NetworkEndpoint peer, CancellationToken cancellationToken = default)
    {
        var localChain = await GetChainSnapshotAsync(cancellationToken);
        var response = await BlockNetworkService.SendAndReceiveAsync(
            peer,
            new NetworkMessage { Type = NetworkMessageType.ChainOffer, Chain = localChain },
            cancellationToken);

        return response?.Type == NetworkMessageType.Chain && response.Chain is not null
            ? await TryAdoptChainAsync(response.Chain, cancellationToken)
            : new ChainAdoptionResult(false, response?.Error ?? "Peer did not return a chain.");
    }

    public async Task BroadcastTransactionAsync(Transaction transaction, IEnumerable<NetworkEndpoint> peers, CancellationToken cancellationToken = default)
    {
        foreach (var peer in peers.Distinct())
        {
            await BlockNetworkService.SendAsync(peer, new NetworkMessage { Type = NetworkMessageType.Transaction, Transaction = transaction }, cancellationToken);
        }
    }

    public async Task BroadcastBlockAsync(Block block, IEnumerable<NetworkEndpoint> peers, CancellationToken cancellationToken = default)
    {
        foreach (var peer in peers.Distinct())
        {
            await BlockNetworkService.SendAsync(peer, new NetworkMessage { Type = NetworkMessageType.Block, Block = block }, cancellationToken);
        }
    }

    private async Task<NetworkMessage?> HandleNetworkMessageAsync(NetworkMessage message, CancellationToken cancellationToken)
    {
        try
        {
            switch (message.Type)
            {
                case NetworkMessageType.Transaction when message.Transaction is not null:
                    await AddTransactionAsync(message.Transaction, cancellationToken);
                    return null;

                case NetworkMessageType.Block when message.Block is not null:
                {
                    var (accepted, error) = await TryAddBlockFromNetAsync(message.Block, cancellationToken);
                    return accepted ? null : new NetworkMessage { Type = NetworkMessageType.Rejected, Error = error };
                }

                case NetworkMessageType.ChainRequest:
                    return new NetworkMessage { Type = NetworkMessageType.Chain, Chain = await GetChainSnapshotAsync(cancellationToken) };

                case NetworkMessageType.ChainOffer when message.Chain is not null:
                    await TryAdoptChainAsync(message.Chain, cancellationToken);
                    return new NetworkMessage { Type = NetworkMessageType.Chain, Chain = await GetChainSnapshotAsync(cancellationToken) };

                case NetworkMessageType.Chain when message.Chain is not null:
                    await TryAdoptChainAsync(message.Chain, cancellationToken);
                    return null;

                default:
                    return new NetworkMessage { Type = NetworkMessageType.Rejected, Error = "Unsupported or incomplete network message." };
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            return new NetworkMessage { Type = NetworkMessageType.Rejected, Error = ex.Message };
        }
    }

    private void CreateGenesisBlock()
    {
        var genesisBlock = new Block(0, [], "Genesis Block", Difficulty) { TimeStamp = DateTime.UnixEpoch };
        _miningService.MineBlock(genesisBlock, Difficulty, showProgress: false, cancelKey: ConsoleKey.None);
        Chain.Add(genesisBlock);
    }

    public async Task<MiningResult> MinePendingTransactionsAsync(
        string minerAddress,
        bool showProgress = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minerAddress);

        Block newBlock;
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var selected = PendingTransactions.OrderByDescending(t => t.Fee).Take(MaxTransactionAmountPerBlock).Select(CloneTransaction).ToList();
            var reward = new Transaction("Coinbase", minerAddress, GetCurrentRewardUnsafe() + selected.Sum(t => t.Fee), 0);
            selected.Insert(0, reward);
            var tip = Chain.Last();
            newBlock = new Block(tip.Index + 1, selected, tip.Hash, Difficulty);
        }
        finally
        {
            _stateGate.Release();
        }

        await _miningLifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_activeMiningTask is not null) throw new InvalidOperationException("Mining is already in progress.");
            _activeMiningTask = _miningService.MineBlockAsync(newBlock, newBlock.Difficulty, showProgress, cancellationToken: cancellationToken);
        }
        finally
        {
            _miningLifecycleGate.Release();
        }

        try
        {
            var result = await _activeMiningTask;
            await _stateGate.WaitAsync(cancellationToken);
            try
            {
                // A sync may have changed the tip while this CPU work was running.
                if (Chain.Last().Hash != newBlock.PrevHash)
                {
                    throw new OperationCanceledException("Mining result is stale because the chain tip changed.", cancellationToken);
                }

                Chain.Add(newBlock);
                RemoveMinedTransactionsUnsafe(newBlock);
                if (newBlock.Index % AdjustmentInterval == 0) AdjustDifficultyUnsafe();
                return result;
            }
            finally
            {
                _stateGate.Release();
            }
        }
        finally
        {
            await _miningLifecycleGate.WaitAsync();
            try { _activeMiningTask = null; }
            finally { _miningLifecycleGate.Release(); }
        }
    }


    public async Task AddTransactionAsync(Transaction newTransaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newTransaction);
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            ValidateTransactionForMempoolUnsafe(newTransaction);
            PendingTransactions.Add(CloneTransaction(newTransaction));
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private void ValidateTransactionForMempoolUnsafe(Transaction transaction)
    {
        if (transaction.From == "Coinbase") throw new InvalidOperationException("Coinbase transactions cannot enter the mempool.");
        if (PendingTransactions.Any(t => t.Id == transaction.Id)) throw new InvalidOperationException("Transaction already exists in the mempool.");
        if (Chain.SelectMany(b => b.Transactions).Any(t => t.Id == transaction.Id)) throw new InvalidOperationException("Transaction already exists in the blockchain.");

        (bool isValid, string errorMessage) validation;
        try { validation = TransactionService.ValidateTransaction(transaction); }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidOperationException("Invalid transaction signature encoding.", ex);
        }
        var (isValid, errorMessage) = validation;
        if (!isValid) throw new InvalidOperationException($"Invalid transaction: {errorMessage}");

        var senderBalance = GetWalletBalanceUnsafe(transaction.From);
        if (senderBalance < transaction.Amount + transaction.Fee)
        {
            throw new InvalidOperationException($"Insufficient balance for transaction from {transaction.From}. Available: {senderBalance}; required: {transaction.Amount + transaction.Fee}.");
        }
    }

    public async Task<(bool accepted, string errorMessage)> TryAddBlockFromNetAsync(Block block, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(block);

        // Validate before cancelling mining; invalid network traffic must not stop a miner.
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            if (!ValidateBlockAgainstPrefix(block, Chain, out var error)) return (false, error);
        }
        finally { _stateGate.Release(); }

        await CancelAndWaitForMiningAsync();
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            // Revalidate after waiting, because another block may have won the race.
            if (!ValidateBlockAgainstPrefix(block, Chain, out var error)) return (false, error);
            Chain.Add(CloneBlock(block));
            RemoveMinedTransactionsUnsafe(block);
            Difficulty = block.Difficulty;
            AdjustDifficultyUnsafe();
            return (true, string.Empty);
        }
        finally { _stateGate.Release(); }
    }

    public async Task<ChainAdoptionResult> TryAdoptChainAsync(IReadOnlyList<Block> candidate, CancellationToken cancellationToken = default)
    {
        if (!IsValidChain(candidate.ToList())) return new ChainAdoptionResult(false, "Received blockchain is invalid.");

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            if (candidate.Count <= Chain.Count) return new ChainAdoptionResult(false, "Received blockchain is not longer than the local blockchain.");
        }
        finally { _stateGate.Release(); }

        await CancelAndWaitForMiningAsync();
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            if (candidate.Count <= Chain.Count) return new ChainAdoptionResult(false, "A newer local blockchain was already accepted.");
            var oldChain = Chain;
            Chain = candidate.Select(CloneBlock).ToList();
            Difficulty = Chain.Last().Difficulty;
            ReconcileMempoolAfterReorgUnsafe(oldChain);
            return new ChainAdoptionResult(true, string.Empty);
        }
        finally { _stateGate.Release(); }
    }

    public bool IsValidChain(List<Block> chain)
    {
        if (chain.Count == 0) return false;
        if (!IsValidGenesis(chain[0])) return false;

        var prefix = new List<Block> { chain[0] };
        for (int i = 1; i < chain.Count; i++)
        {
            if (!ValidateBlockAgainstPrefix(chain[i], prefix, out _)) return false;
            prefix.Add(chain[i]);
        }
        return true;
    }

    private static bool IsValidGenesis(Block genesis) =>
        genesis.Index == 0 &&
        genesis.PrevHash == "Genesis Block" &&
        genesis.TimeStamp == DateTime.UnixEpoch &&
        genesis.Transactions.Count == 0 &&
        genesis.Hash == HashingService.ComputeHash(genesis) &&
        genesis.Hash.StartsWith(new string('0', genesis.Difficulty), StringComparison.Ordinal);

    private bool ValidateBlockAgainstPrefix(Block block, IReadOnlyList<Block> prefix, out string error)
    {
        var latest = prefix.Last();
        if (block.Index != latest.Index + 1)
        {
            error = block.Index > latest.Index + 1
                ? "Local node is behind; blockchain synchronization is required."
                : "Invalid block index.";
            return false;
        }
        if (block.PrevHash != latest.Hash) { error = "Invalid previous hash."; return false; }
        if (block.Hash != HashingService.ComputeHash(block)) { error = "Invalid block hash."; return false; }
        if (!block.Hash.StartsWith(new string('0', block.Difficulty), StringComparison.Ordinal)) { error = "Invalid proof of work."; return false; }
        if (prefix.Any(b => b.Hash == block.Hash)) { error = "Block already exists in the blockchain."; return false; }
        if (block.Transactions.Count is < 1 or > MaxTransactionAmountPerBlock + 1) { error = "Invalid transaction count."; return false; }
        if (block.Transactions[0].From != "Coinbase" || block.Transactions.Count(t => t.From == "Coinbase") != 1) { error = "Block must start with exactly one reward transaction."; return false; }

        var ids = new HashSet<Guid>();
        var chainTransactionIds = prefix.SelectMany(b => b.Transactions).Select(t => t.Id).ToHashSet();
        decimal fees = 0;
        var blockBalanceChanges = new Dictionary<string, decimal>();
        foreach (var transaction in block.Transactions)
        {
            if (!ids.Add(transaction.Id)) { error = "Block contains a duplicate transaction."; return false; }
            if (chainTransactionIds.Contains(transaction.Id)) { error = "Transaction already exists in the blockchain."; return false; }
            (bool valid, string validationError) validation;
            try { validation = TransactionService.ValidateTransaction(transaction); }
            catch (Exception) { error = "Transaction has malformed signature data."; return false; }
            var (valid, validationError) = validation;
            if (!valid) { error = $"Invalid transaction in block: {validationError}"; return false; }
            if (transaction.From == "Coinbase")
            {
                AddBalanceChange(blockBalanceChanges, transaction.To, transaction.Amount);
                continue;
            }

            decimal available = GetConfirmedBalance(prefix, transaction.From) + GetBalanceChange(blockBalanceChanges, transaction.From);
            if (available < transaction.Amount + transaction.Fee) { error = "Transaction spends more than its confirmed balance."; return false; }
            AddBalanceChange(blockBalanceChanges, transaction.From, -(transaction.Amount + transaction.Fee));
            AddBalanceChange(blockBalanceChanges, transaction.To, transaction.Amount);
            fees += transaction.Fee;
        }

        int halvings = block.Index / HalvingInterval;
        decimal expectedReward = RewardAmount / (decimal)Math.Pow(2, halvings) + fees;
        if (block.Transactions[0].Amount != expectedReward || block.Transactions[0].Fee != 0)
        {
            error = "Invalid reward transaction.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public decimal GetWalletBalance(string walletAddress)
    {
        _stateGate.Wait();
        try { return GetWalletBalanceUnsafe(walletAddress); }
        finally { _stateGate.Release(); }
    }

    private decimal GetWalletBalanceUnsafe(string walletAddress)
    {
        decimal balance = 0;
        foreach (var transaction in Chain.SelectMany(block => block.Transactions))
        {
            if (transaction.From == walletAddress) balance -= transaction.Amount + transaction.Fee;
            if (transaction.To == walletAddress) balance += transaction.Amount;
        }
        balance -= PendingTransactions.Where(t => t.From == walletAddress).Sum(t => t.Amount + t.Fee);
        return balance;
    }

    public decimal GetTotalSupply()
    {
        _stateGate.Wait();
        try { return Chain.SelectMany(b => b.Transactions).Where(t => t.From == "Coinbase").Sum(t => t.Amount); }
        finally { _stateGate.Release(); }
    }

    public decimal GetCurrentReward()
    {
        _stateGate.Wait();
        try { return GetCurrentRewardUnsafe(); }
        finally { _stateGate.Release(); }
    }

    private decimal GetCurrentRewardUnsafe() => RewardAmount / (decimal)Math.Pow(2, Chain.Count / HalvingInterval);

    private void AdjustDifficultyUnsafe()
    {
        if (Chain.Count <= AdjustmentInterval) return;
        var last = Chain.Last();
        var previous = Chain[Chain.Count - AdjustmentInterval];
        var millisecondsPerBlock = (last.TimeStamp - previous.TimeStamp).TotalMilliseconds / AdjustmentInterval;
        if (millisecondsPerBlock < TargetTimePerBlock) Difficulty++;
        else if (millisecondsPerBlock > TargetTimePerBlock && Difficulty > 1) Difficulty--;
    }

    private static decimal GetConfirmedBalance(IEnumerable<Block> chain, string walletAddress) => chain
        .SelectMany(block => block.Transactions)
        .Sum(transaction => transaction.From == walletAddress ? -(transaction.Amount + transaction.Fee) : transaction.To == walletAddress ? transaction.Amount : 0);

    private static decimal GetBalanceChange(IReadOnlyDictionary<string, decimal> changes, string walletAddress) =>
        changes.TryGetValue(walletAddress, out decimal change) ? change : 0;

    private static void AddBalanceChange(IDictionary<string, decimal> changes, string walletAddress, decimal amount) =>
        changes[walletAddress] = (changes.TryGetValue(walletAddress, out decimal change) ? change : 0) + amount;

    private void RemoveMinedTransactionsUnsafe(Block block)
    {
        var minedIds = block.Transactions.Where(t => t.From != "Coinbase").Select(t => t.Id).ToHashSet();
        PendingTransactions.RemoveAll(t => minedIds.Contains(t.Id));
    }

    private void ReconcileMempoolAfterReorgUnsafe(IEnumerable<Block> oldChain)
    {
        var confirmedIds = Chain.SelectMany(b => b.Transactions).Select(t => t.Id).ToHashSet();
        var candidates = PendingTransactions.Concat(oldChain.SelectMany(b => b.Transactions).Where(t => t.From != "Coinbase"))
            .Where(t => !confirmedIds.Contains(t.Id)).GroupBy(t => t.Id).Select(g => g.First()).OrderByDescending(t => t.Fee).ToList();
        PendingTransactions = [];
        foreach (var transaction in candidates)
        {
            try { ValidateTransactionForMempoolUnsafe(transaction); PendingTransactions.Add(CloneTransaction(transaction)); }
            catch (InvalidOperationException) { /* Transactions invalid under the new branch are discarded. */ }
        }
    }

    private async Task CancelAndWaitForMiningAsync()
    {
        Task<MiningResult>? active;
        await _miningLifecycleGate.WaitAsync();
        try { active = _activeMiningTask; _miningService.CancelMining(); }
        finally { _miningLifecycleGate.Release(); }

        if (active is null) return;
        try { await active; }
        catch (OperationCanceledException) { /* Expected when a valid update changes the parent tip. */ }
    }

    private async Task<List<Block>> GetChainSnapshotAsync(CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try { return Chain.Select(CloneBlock).ToList(); }
        finally { _stateGate.Release(); }
    }

    public List<Block> GetChainSnapshot()
    {
        _stateGate.Wait();
        try { return Chain.Select(CloneBlock).ToList(); }
        finally { _stateGate.Release(); }
    }

    public List<Transaction> GetPendingTransactionsSnapshot()
    {
        _stateGate.Wait();
        try { return PendingTransactions.Select(CloneTransaction).ToList(); }
        finally { _stateGate.Release(); }
    }

    public void SaveChain()
    {
        _stateGate.Wait();
        try
        {
            var temporaryPath = ChainFilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Chain, JsonOptions));
            File.Move(temporaryPath, ChainFilePath, overwrite: true);
        }
        finally { _stateGate.Release(); }
    }

    public void LoadChain()
    {
        if (!File.Exists(ChainFilePath)) throw new InvalidOperationException("No existing blockchain found.");
        var loaded = JsonSerializer.Deserialize<List<Block>>(File.ReadAllText(ChainFilePath));
        if (loaded is null || !IsValidChain(loaded)) throw new InvalidOperationException("Failed to load blockchain.");
        Chain = loaded;
        Difficulty = Chain.Last().Difficulty;
    }

    private static Transaction CloneTransaction(Transaction transaction) => (Transaction)transaction.Clone();

    private static Block CloneBlock(Block block) => new(block.Index, block.Transactions.Select(CloneTransaction).ToList(), block.PrevHash, block.Difficulty)
    {
        TimeStamp = block.TimeStamp,
        MerkleRoot = block.MerkleRoot,
        Nonce = block.Nonce,
        Hash = block.Hash,
    };

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _miningService.CancelMining();
        await _listenerLifecycleGate.WaitAsync();
        try { await StopListenerUnsafeAsync(); }
        finally { _listenerLifecycleGate.Release(); }
        await _networkService.DisposeAsync();
        _shutdown.Dispose();
        _stateGate.Dispose();
        _miningLifecycleGate.Dispose();
        _listenerLifecycleGate.Dispose();
    }
}
