using System;
using System.Collections.Generic;
using System.Text;

namespace BlockChain.Models
{
    public class Transaction : ICloneable
    {
        public Guid Id { get; private set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public DateTime TimeStamp { get; set; }
        public byte[] Signature { get; set; } = [];

        public Transaction(string from, string to, decimal amount, decimal fee)
        {
            Id = Guid.NewGuid();
            From = from;
            To = to;
            Amount = amount;
            Fee = fee;
            TimeStamp = DateTime.UtcNow;
        }

        public Transaction() : this(string.Empty, string.Empty, 0, 0) { }

        public string ToRowString()
        {
            return $"{Id}{From}{To}{Amount}{Fee}{TimeStamp}";
        }

        public byte[] GetDataToSign()
        {
            return Encoding.UTF8.GetBytes(ToRowString());
        }

        public override string ToString()
        {
            return $"Transaction ID: {Id}\nFrom: {From}\nTo: {To}\nAmount: {Amount}\nFee: {Fee}\nTimeStamp: {TimeStamp:o}";
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
