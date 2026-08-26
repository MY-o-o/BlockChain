using BlockChain.Models;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BlockChain.Services;

public enum NetworkMessageType
{
    Transaction,
    Block,
    ChainRequest,
    ChainOffer,
    Chain,
    Rejected,
}

/// <summary>A single line-delimited message exchanged by two nodes.</summary>
public sealed class NetworkMessage
{
    public NetworkMessageType Type { get; init; }
    public Transaction? Transaction { get; init; }
    public Block? Block { get; init; }
    public List<Block>? Chain { get; init; }
    public string? Error { get; init; }
}

public readonly record struct NetworkEndpoint(string Host, int Port);

/// <summary>
/// Owns a TCP listener. Each TCP connection carries one JSON message and an
/// optional JSON response, so the listening port remains available continuously.
/// </summary>
public sealed class BlockNetworkService : IAsyncDisposable
{
    private const int MaximumMessageBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private TcpListener? _listener;

    public async Task ListenAsync(
        int port,
        Func<NetworkMessage, CancellationToken, Task<NetworkMessage?>> handleMessageAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handleMessageAsync);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _ = HandleClientAsync(client, handleMessageAsync, cancellationToken);
            }
        }
        finally
        {
            _listener.Stop();
            _listener = null;
        }
    }

    public static async Task SendAsync(NetworkEndpoint endpoint, NetworkMessage message, CancellationToken cancellationToken = default)
    {
        _ = await SendAndReceiveAsync(endpoint, message, cancellationToken);
    }

    public static async Task<NetworkMessage?> SendAndReceiveAsync(
        NetworkEndpoint endpoint,
        NetworkMessage message,
        CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
        using var stream = client.GetStream();
        await WriteMessageAsync(stream, message, cancellationToken);
        return await ReadMessageAsync(stream, cancellationToken);
    }

    public static Task SendBlockAsync(Block block, string ipAddress, int port, CancellationToken cancellationToken = default) =>
        SendAsync(new NetworkEndpoint(ipAddress, port), new NetworkMessage { Type = NetworkMessageType.Block, Block = block }, cancellationToken);

    private static async Task HandleClientAsync(
        TcpClient client,
        Func<NetworkMessage, CancellationToken, Task<NetworkMessage?>> handleMessageAsync,
        CancellationToken serverCancellationToken)
    {
        using (client)
        {
            using var stream = client.GetStream();
            try
            {
                var message = await ReadMessageAsync(stream, serverCancellationToken);
                if (message is null) return;

                var response = await handleMessageAsync(message, serverCancellationToken);
                if (response is not null)
                {
                    await WriteMessageAsync(stream, response, serverCancellationToken);
                }
            }
            catch (Exception) when (!serverCancellationToken.IsCancellationRequested)
            {
                // A malformed or disconnected peer must not bring down the listener.
            }
        }
    }

    private static async Task WriteMessageAsync(Stream stream, NetworkMessage message, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(message, JsonOptions);
        byte[] data = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(data, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<NetworkMessage?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? json = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (Encoding.UTF8.GetByteCount(json) > MaximumMessageBytes)
        {
            throw new InvalidDataException("Network message is too large.");
        }

        return JsonSerializer.Deserialize<NetworkMessage>(json, JsonOptions)
            ?? throw new InvalidDataException("Network message could not be deserialized.");
    }

    public ValueTask DisposeAsync()
    {
        _listener?.Stop();
        return ValueTask.CompletedTask;
    }
}
