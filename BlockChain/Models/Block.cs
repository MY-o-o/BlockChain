using System;
using System.Collections.Generic;
using System.Text;

namespace BlockChain.Models
{
    public class Block(int index, List<Transaction> transactions, string prevHash, int difficulty)
    {
        public int Index { get; set; } = index;
        public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
        public List<Transaction> Transactions { get; set; } = transactions;
        public string PrevHash { get; set; } = prevHash;
        public int Difficulty { get; set; } = difficulty;
        public long Nonce { get; set; } = 0;
        public string Hash { get; set; } = string.Empty;

        public string ToRowString(bool includeNonce = true)
        {
            string transactionsRow = string.Concat(Transactions.Select(t => t.ToRowString()));

            return $"{Index}{TimeStamp:o}{transactionsRow}{PrevHash}{Difficulty}{(includeNonce ? Nonce : string.Empty)}";
        }
    }
}
