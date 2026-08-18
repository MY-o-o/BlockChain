using BlockChain.Models;
using System.Security.Cryptography;

namespace BlockChain.Services
{
    public class WalletService
    {
        public Wallet CreateWallet(string name)
        {
            using (var ecdsa = ECDsa.Create())
            {
                var privateKey = ecdsa.ExportECPrivateKey();
                var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
                var address = Convert.ToBase64String(publicKey); // Simplified address generation
                return new Wallet(name, address, publicKey, privateKey);
            }
        }

        public bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey)
        {
            using (var ecdsa = ECDsa.Create())
            {
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
            }
        }
    }
}
