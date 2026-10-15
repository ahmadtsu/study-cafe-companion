using System.Diagnostics;
using System.Text.Json;
using Supabase.Realtime;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.Models;

namespace StudyCafe.Companion.Core;

public sealed class SupabaseRealtimeService : IDisposable
{
    private const string ChannelName = "study-cafe-room-main";
    private readonly TimerCountdownEngine _engine;
    private Client? _client;
    private RealtimeChannel? _channel;
    private bool _disposed;

    public SupabaseRealtimeService(TimerCountdownEngine engine)
    {
        _engine = engine;
    }

    public async Task StartAsync()
    {
        var settings = AppSettings.Current;
        if (string.IsNullOrWhiteSpace(settings.SupabaseUrl) || string.IsNullOrWhiteSpace(settings.SupabaseAnonKey))
        {
            Debug.WriteLine("SupabaseRealtimeService: Supabase URL or AnonKey not configured. Standing by for local sync.");
            return;
        }

        try
        {
            string baseUrl = settings.SupabaseUrl.Trim().TrimEnd('/');
            string wsUrl = baseUrl
                .Replace("https://", "wss://")
                .Replace("http://", "ws://");

            if (!wsUrl.EndsWith("/realtime/v1"))
            {
                wsUrl += "/realtime/v1";
            }

            var options = new ClientOptions
            {
                Parameters = { ApiKey = settings.SupabaseAnonKey },
            };

            _client = new Client(wsUrl, options);
            await _client.ConnectAsync();

            _channel = _client.Channel(ChannelName);

            if (_channel != null)
            {
                // Register broadcast handler
                _channel.Broadcast()?.AddBroadcastEventHandler(OnBroadcastMessage);

                await _channel.Subscribe();

                // Request current room state from any active browser tab
                try
                {
                    var broadcast = _channel.Broadcast();
                    if (broadcast != null)
                    {
                        await broadcast.Send("REQUEST_ROOM_SYNC", new { });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to send REQUEST_ROOM_SYNC: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SupabaseRealtimeService error: {ex.Message}");
        }
    }

    private void OnBroadcastMessage(IRealtimeBroadcast sender, BaseBroadcast? msg)
    {
        if (msg?.Payload == null) return;

        try
        {
            var json = JsonSerializer.Serialize(msg.Payload);

            switch (msg.Event)
            {
                case "START_POMO":
                {
                    var payload = JsonSerializer.Deserialize<StartPomoPayload>(json);
                    if (payload != null)
                    {
                        _engine.HandlePomoStart(payload.DurationMinutes, payload.StartedAt);
                    }
                    break;
                }
                case "START_BREAK":
                {
                    var payload = JsonSerializer.Deserialize<StartBreakPayload>(json);
                    if (payload != null)
                    {
                        _engine.HandleBreakStart(payload.DurationMinutes, payload.StartedAt);
                    }
                    break;
                }
                case "BREAK_ENDED":
                {
                    var payload = JsonSerializer.Deserialize<BreakEndedPayload>(json);
                    _engine.HandleBreakEnded(payload?.BreakZeroReachedAt);
                    break;
                }
                case "TOGGLE_PAUSE":
                {
                    var payload = JsonSerializer.Deserialize<TogglePausePayload>(json);
                    if (payload != null)
                    {
                        _engine.HandleTogglePause(payload.IsPaused, payload.PausedRemainingSec, payload.PausedAt);
                    }
                    break;
                }
                case "RESET_ROOM":
                {
                    _engine.HandleReset();
                    break;
                }
                case "RESPONSE_ROOM_SYNC":
                {
                    var payload = JsonSerializer.Deserialize<RoomSyncPayload>(json);
                    if (payload?.State != null)
                    {
                        _engine.ApplyRoomState(payload.State);
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error processing broadcast message ({msg.Event}): {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _channel?.Unsubscribe();
            _client?.Disconnect();
        }
        catch { }
    }
}
