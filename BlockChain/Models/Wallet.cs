using System;
using System.Collections.Generic;
using System.Text;

namespace BlockChain.Models
{
    public class Wallet
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public byte[] PublicKey { get; }
        public byte[] PrivateKey { get; set; }

        public Wallet() { }
        public Wallet(string name, string address, byte[] publicKey, byte[] privateKey)
        {
            Name = name;
            Address = address;
            PublicKey = publicKey;
            PrivateKey = privateKey;
        }

        public byte[] Sign(byte[] data)
        {
            using (var ecdsa = System.Security.Cryptography.ECDsa.Create())
            {
                ecdsa.ImportECPrivateKey(PrivateKey, out _);
                return ecdsa.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA256);
            }
        }
    }
}
