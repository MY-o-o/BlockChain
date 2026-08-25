using BlockChain.Models;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace BlockChain.Services
{
    public enum DataType
    {
        Transaction,
        Block,
        BlockChain,
    }

    public readonly record struct UnifiedTransferData(
        DataType Type,
        Transaction Transaction,
        Block Block,
        List<Block> Chain);

    public class BlockNetworkService
    {
        //implement class DataMessage
        public static async Task<Block>ReceiveBlockAsync(int port, CancellationToken token = default)
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            using var client = await listener.AcceptTcpClientAsync(token);

            using var reader = new StreamReader(client.GetStream());
            var blockJson = await reader.ReadToEndAsync(token);

            listener.Stop();

            var block = JsonSerializer.Deserialize<Block>(blockJson);
            return block;
        }

        public static async Task SendBlockAsync(Block block, string ipAddress, int port)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(ipAddress, port);

            using var writer = new StreamWriter(client.GetStream());
            var blockJson = JsonSerializer.Serialize(block);

            await writer.WriteAsync(blockJson);
            await writer.FlushAsync();
            client.Close();
        }
    }
}
