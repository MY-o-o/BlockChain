using BlockChain.Services;

namespace BlockChain.Models
{
    public class Block(int index, List<Transaction> transactions, string prevHash, int difficulty)
    {
        public int Index { get; set; } = index;
        public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
        public List<Transaction> Transactions { get; set; } = transactions;
        public string MerkleRoot { get; set; } = string.Empty;
        public string PrevHash { get; set; } = prevHash;
        public int Difficulty { get; set; } = difficulty;
        public long Nonce { get; set; } = 0;
        public string Hash { get; set; } = string.Empty;

        public string ToRowString(bool includeNonce = true)
        {
            CalculateMerkleRoot();

            return $"{Index}{TimeStamp:o}{MerkleRoot}{PrevHash}{Difficulty}{(includeNonce ? Nonce : string.Empty)}";
        }

        public void CalculateMerkleRoot()
        {
            if (Transactions.Count == 0)
            {
                MerkleRoot = HashingService.ComputeHash(string.Empty);
                return;
            }

            List<string> transactionHashes = 
                Transactions
                    .Select(t => HashingService.ComputeHash(t.ToRowString()))
                    .ToList();

            var merkleRoot = CompressHashes(transactionHashes);

            if (merkleRoot == null || merkleRoot.Count != 1)
            {
                throw new InvalidOperationException("Merkle Root was calculated wrong!");
            }

            MerkleRoot = merkleRoot.First();
        }

        private static List<string> CompressHashes(List<string> hashes)
        {
            if (hashes == null || hashes.Count == 0) throw new InvalidOperationException("Hashes can not be compressed!");
            if (hashes.Count == 1) return hashes;

            List<string> combinedHashes = [];
            for (int i = 0; i < hashes.Count; i += 2)
            {
                int secondHashIndex = i + 1 < hashes.Count ? i + 1 : i;
                string combinedHash = HashingService.ComputeHash(hashes[i] + hashes[secondHashIndex]);
                combinedHashes.Add(combinedHash);
            }

            return CompressHashes(combinedHashes);
        }
    }
}
