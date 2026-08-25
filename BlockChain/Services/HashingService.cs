using BlockChain.Models;
using System.Text;

namespace BlockChain.Services
{
    public static class HashingService
    {
        public static string ComputeHash(Block block)
        {
            return ComputeHash(block.ToRowString());
        }

        public static string ComputeHash(string input)
        {
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = System.Security.Cryptography.SHA256.HashData(inputBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
