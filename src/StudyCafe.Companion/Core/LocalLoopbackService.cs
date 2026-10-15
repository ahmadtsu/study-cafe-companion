using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace StudyCafe.Companion.Core;

/// <summary>
/// Lightweight, non-elevated local loopback HTTP service on 127.0.0.1:47800 using standard TcpListener.
/// Avoids Windows http.sys URL ACL permission issues while providing full CORS and
/// Chrome/Edge Private Network Access (PNA) compatibility.
/// </summary>
public sealed class LocalLoopbackService : IDisposable
{
    private readonly TcpListener _listener;
    private readonly TimerCountdownEngine _engine;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public LocalLoopbackService(TimerCountdownEngine engine, int port = 47800)
    {
        _engine = engine;
        _listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            _listener.Start();
            Task.Run(ListenLoop);
            Debug.WriteLine($"LocalLoopbackService: listening on http://127.0.0.1:{port}/");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalLoopbackService: could not bind port {port}: {ex.Message}");
        }
    }

    private async Task ListenLoop()
    {
        while (!_disposed)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_disposed) break;
                Debug.WriteLine($"LocalLoopbackService accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

                // Read request line (e.g. "POST /sync HTTP/1.1")
                var requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                var parts = requestLine.Split(' ', 3);
                if (parts.Length < 2) return;

                var method = parts[0].ToUpperInvariant();
                var path = parts[1];

                // Parse headers
                int contentLength = 0;
                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
                {
                    if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        var lenStr = headerLine["Content-Length:".Length..].Trim();
                        int.TryParse(lenStr, out contentLength);
                    }
                }

                // Handle CORS preflight & PNA
                if (method == "OPTIONS")
                {
                    await SendResponseAsync(stream, 204, "No Content", "text/plain", string.Empty);
                    return;
                }

                // Handle POST /sync
                if (method == "POST" && (path == "/sync" || path.StartsWith("/sync?")))
                {
                    string body = "";
                    if (contentLength > 0)
                    {
                        char[] buffer = new char[contentLength];
                        int totalRead = 0;
                        while (totalRead < contentLength)
                        {
                            int read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                            if (read <= 0) break;
                            totalRead += read;
                        }
                        body = new string(buffer, 0, totalRead);
                    }

                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            var state = JsonSerializer.Deserialize<RoomStateModel>(body, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (state != null)
                            {
                                _engine.ApplyRoomState(state);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to parse sync body: {ex.Message}");
                        }
                    }

                    await SendResponseAsync(stream, 200, "OK", "application/json", "{\"ok\":true}");
                    return;
                }

                // Handle GET /health
                if (method == "GET" && (path == "/health" || path.StartsWith("/health?")))
                {
                    await SendResponseAsync(stream, 200, "OK", "application/json", "{\"status\":\"running\"}");
                    return;
                }

                // Default 404
                await SendResponseAsync(stream, 404, "Not Found", "text/plain", "Not Found");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LocalLoopbackService error handling client: {ex.Message}");
            }
        }
    }

    private static async Task SendResponseAsync(
        NetworkStream stream,
        int statusCode,
        string statusText,
        string contentType,
        string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        var headerSb = new StringBuilder();
        headerSb.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        headerSb.Append("Access-Control-Allow-Origin: *\r\n");
        headerSb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
        headerSb.Append("Access-Control-Allow-Headers: Content-Type, Authorization\r\n");
        headerSb.Append("Access-Control-Allow-Private-Network: true\r\n");
        headerSb.Append($"Content-Type: {contentType}; charset=utf-8\r\n");
        headerSb.Append($"Content-Length: {bodyBytes.Length}\r\n");
        headerSb.Append("Connection: close\r\n");
        headerSb.Append("\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(headerSb.ToString());
        await stream.WriteAsync(headerBytes);
        if (bodyBytes.Length > 0)
        {
            await stream.WriteAsync(bodyBytes);
        }
        await stream.FlushAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();
            _listener.Stop();
        }
        catch { }
    }
}
