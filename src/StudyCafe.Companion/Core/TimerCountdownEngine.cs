using System.Windows.Threading;

namespace StudyCafe.Companion.Core;

public sealed class TimerCountdownEngine
{
    private readonly DispatcherTimer _timer;
    private readonly object _lock = new();

    private string _phase = "idle";
    private int _durationSec = 25 * 60;
    private long? _startedAt;
    private bool _isPaused;
    private int? _pausedRemainingSec;
    private int _participants;

    public event Action<string, string, int, bool>? StateChanged;

    public TimerCountdownEngine()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void ApplyRoomState(RoomStateModel state)
    {
        lock (_lock)
        {
            _phase = state.Phase;
            _durationSec = state.DurationSec;
            _startedAt = state.StartedAt;
            _isPaused = state.IsPaused;
            _pausedRemainingSec = state.PausedRemainingSec;
            if (state.Participants != null)
            {
                _participants = state.Participants.Count;
            }
        }
        Tick();
    }

    public void HandlePomoStart(int durationMinutes, long startedAt)
    {
        lock (_lock)
        {
            _phase = "work";
            _durationSec = durationMinutes * 60;
            _startedAt = startedAt;
            _isPaused = false;
            _pausedRemainingSec = null;
        }
        Tick();
    }

    public void HandleBreakStart(int durationMinutes, long startedAt)
    {
        lock (_lock)
        {
            _phase = "break";
            _durationSec = durationMinutes * 60;
            _startedAt = startedAt;
            _isPaused = false;
            _pausedRemainingSec = null;
        }
        Tick();
    }

    public void HandleBreakEnded()
    {
        lock (_lock)
        {
            if (_phase == "break")
            {
                _phase = "break_ended";
            }
        }
        Tick();
    }

    public void HandleTogglePause(bool isPaused, int? pausedRemainingSec, long? pausedAt)
    {
        lock (_lock)
        {
            _isPaused = isPaused;
            _pausedRemainingSec = pausedRemainingSec;
            if (_phase == "idle" && isPaused)
            {
                _phase = "work";
            }
        }
        Tick();
    }

    public void HandleReset()
    {
        lock (_lock)
        {
            _phase = "idle";
            _startedAt = null;
            _isPaused = false;
            _pausedRemainingSec = null;
        }
        Tick();
    }

    public void SetParticipantCount(int count)
    {
        lock (_lock)
        {
            _participants = count;
        }
        Tick();
    }

    public void Tick()
    {
        string phase;
        int durationSec;
        long? startedAt;
        bool isPaused;
        int? pausedRemainingSec;
        int participants;

        lock (_lock)
        {
            phase = _phase;
            durationSec = _durationSec;
            startedAt = _startedAt;
            isPaused = _isPaused;
            pausedRemainingSec = _pausedRemainingSec;
            participants = _participants;
        }

        string formatted;

        if (isPaused && pausedRemainingSec.HasValue)
        {
            int remainingSec = pausedRemainingSec.Value;
            int mins = remainingSec / 60;
            int secs = remainingSec % 60;
            if (mins >= 60)
            {
                int hrs = mins / 60;
                mins %= 60;
                formatted = $"{hrs}:{mins:D2}:{secs:D2}";
            }
            else
            {
                formatted = $"{mins:D2}:{secs:D2}";
            }
        }
        else if (phase == "idle" || startedAt == null)
        {
            formatted = "Ready";
        }
        else
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long elapsedMs = Math.Max(0, now - startedAt.Value);
            int elapsedSec = (int)(elapsedMs / 1000);
            int remainingSec = Math.Max(0, durationSec - elapsedSec);

            int mins = remainingSec / 60;
            int secs = remainingSec % 60;
            if (mins >= 60)
            {
                int hrs = mins / 60;
                mins %= 60;
                formatted = $"{hrs}:{mins:D2}:{secs:D2}";
            }
            else
            {
                formatted = $"{mins:D2}:{secs:D2}";
            }
        }

        StateChanged?.Invoke(formatted, phase, participants, isPaused);
    }
}
