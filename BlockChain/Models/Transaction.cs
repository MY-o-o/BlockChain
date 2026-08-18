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
        public DateTime TimeStamp { get; set; }
        public byte[] Signature { get; set; } = [];

        public Transaction(string from, string to, decimal amount)
        {
            Id = Guid.NewGuid();
            From = from;
            To = to;
            Amount = amount;
            TimeStamp = DateTime.UtcNow;
        }

        public Transaction() : this(string.Empty, string.Empty, 0) { }

        public string ToRowString()
        {
            return $"{Id}{From}{To}{Amount}{TimeStamp}";
        }

        public byte[] GetDataToSign()
        {
            return Encoding.UTF8.GetBytes(ToRowString());
        }

        public override string ToString()
        {
            return $"Transaction [Id={Id}, From={From}, To={To}, Amount={Amount}, TimeStamp={TimeStamp:o}]";
        }

        public object Clone()
        {
            return new Transaction(From, To, Amount)
            {
                Id = Id,
                Signature = Signature,
                TimeStamp = TimeStamp
            };
        }
    }
}
