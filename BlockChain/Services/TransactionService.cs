using BlockChain.Models;

namespace BlockChain.Services
{
    public class TransactionService
    {
        public static Transaction CreateTransaction(string from, string to, decimal amount, decimal fee, Wallet wallet)
        {
            var tx = new Transaction(from, to, amount, fee);
            tx.Signature = wallet.Sign(tx.GetDataToSign());
            return tx;
        }

        public static (bool isValid, string errorMessage) ValidateTransaction(Transaction transaction)
        {
            if (transaction.From == "Coinbase")
            {
                return (true, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(transaction.From))
            {
                return (false, "Sender address is required.");
            }
            if (string.IsNullOrWhiteSpace(transaction.To))
            {
                return (false, "Recipient address is required.");
            }
            if (transaction.Amount <= 0)
            {
                return (false, "Transaction amount must be greater than zero.");
            }
            if (transaction.Signature == null || transaction.Signature.Length == 0)
            {
                return (false, "Transaction signature is required.");
            }
            if (!WalletService.VerifySignature(transaction.GetDataToSign(), transaction.Signature, Convert.FromBase64String(transaction.From)))
            {
                return (false, "Invalid transaction signature.");
            }

            return (true, string.Empty);
        }
    }
}
