using System.Text;

namespace BlockChain.Models
{
    public class Transaction(string from, string to, decimal amount, decimal fee) : ICloneable
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string From { get; set; } = from;
        public string To { get; set; } = to;
        public decimal Amount { get; set; } = amount;
        public decimal Fee { get; set; } = fee;
        public DateTime TimeStamp { get; set; } = DateTime.UtcNow;
        public byte[] Signature { get; set; } = [];

        public Transaction() : this(string.Empty, string.Empty, 0, 0) { }

        public string ToRowString(bool isSigned = true)
        {
            return $"{Id}{From}{To}{Amount}{Fee}{TimeStamp}{(isSigned ? Convert.ToBase64String(Signature) : string.Empty)}";
        }

        public byte[] GetDataToSign()
        {
            return Encoding.UTF8.GetBytes(ToRowString());
        }

        public object Clone()
        {
            return new Transaction(From, To, Amount, Fee)
            {
                Id = Id,
                Signature = Signature,
                TimeStamp = TimeStamp
            };
        }
    }
}
