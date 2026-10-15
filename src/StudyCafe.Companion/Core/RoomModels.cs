using System.Text.Json.Serialization;

namespace StudyCafe.Companion.Core;

public sealed class RoomParticipant
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("joinedAt")]
    public long JoinedAt { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "active";
}

public sealed class RoomStateModel
{
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "idle";

    [JsonPropertyName("durationSec")]
    public int DurationSec { get; set; }

    [JsonPropertyName("startedAt")]
    public long? StartedAt { get; set; }

    [JsonPropertyName("isPaused")]
    public bool IsPaused { get; set; }

    [JsonPropertyName("pausedRemainingSec")]
    public int? PausedRemainingSec { get; set; }

    [JsonPropertyName("pausedAt")]
    public long? PausedAt { get; set; }

    [JsonPropertyName("breakZeroReachedAt")]
    public long? BreakZeroReachedAt { get; set; }

    [JsonPropertyName("leftTableAt")]
    public long? LeftTableAt { get; set; }

    [JsonPropertyName("participants")]
    public List<RoomParticipant>? Participants { get; set; }

    [JsonIgnore]
    public int ParticipantCount => Participants?.Count ?? 0;
}

public sealed class BreakEndedPayload
{
    [JsonPropertyName("breakZeroReachedAt")]
    public long? BreakZeroReachedAt { get; set; }
}

public sealed class StartPomoPayload
{
    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonPropertyName("startedAt")]
    public long StartedAt { get; set; }

    [JsonPropertyName("startedBy")]
    public string? StartedBy { get; set; }
}

public sealed class StartBreakPayload
{
    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonPropertyName("startedAt")]
    public long StartedAt { get; set; }
}

public sealed class TogglePausePayload
{
    [JsonPropertyName("isPaused")]
    public bool IsPaused { get; set; }

    [JsonPropertyName("pausedRemainingSec")]
    public int? PausedRemainingSec { get; set; }

    [JsonPropertyName("pausedAt")]
    public long? PausedAt { get; set; }
}

public sealed class RoomSyncPayload
{
    [JsonPropertyName("state")]
    public RoomStateModel? State { get; set; }

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; set; }
}
