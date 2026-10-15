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
    private long? _breakZeroReachedAt;
    private long? _leftTableAt;

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
            _breakZeroReachedAt = state.BreakZeroReachedAt;
            _leftTableAt = state.LeftTableAt;

            if (_phase == "left_table" && _leftTableAt == null)
            {
                _leftTableAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            if (_phase == "break_ended" && _breakZeroReachedAt == null)
            {
                if (_startedAt.HasValue && _durationSec > 0)
                {
                    _breakZeroReachedAt = _startedAt.Value + (long)_durationSec * 1000;
                }
                else
                {
                    _breakZeroReachedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }

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
            _breakZeroReachedAt = null;
            _leftTableAt = null;
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
            _breakZeroReachedAt = null;
            _leftTableAt = null;
        }
        Tick();
    }

    public void HandleBreakEnded(long? breakZeroReachedAt = null)
    {
        lock (_lock)
        {
            _phase = "break_ended";
            _leftTableAt = null;
            if (breakZeroReachedAt.HasValue)
            {
                _breakZeroReachedAt = breakZeroReachedAt.Value;
            }
            else if (_breakZeroReachedAt == null)
            {
                if (_startedAt.HasValue && _durationSec > 0)
                {
                    _breakZeroReachedAt = _startedAt.Value + (long)_durationSec * 1000;
                }
                else
                {
                    _breakZeroReachedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }
        }
        Tick();
    }

    public void HandleLeaveTable(long? leftTableAt = null)
    {
        lock (_lock)
        {
            _phase = "left_table";
            _leftTableAt = leftTableAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _isPaused = false;
            _pausedRemainingSec = null;
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
            _breakZeroReachedAt = null;
            _leftTableAt = null;
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
        long? breakZeroReachedAt;
        long? leftTableAt;

        lock (_lock)
        {
            phase = _phase;
            durationSec = _durationSec;
            startedAt = _startedAt;
            isPaused = _isPaused;
            pausedRemainingSec = _pausedRemainingSec;
            participants = _participants;
            breakZeroReachedAt = _breakZeroReachedAt;
            leftTableAt = _leftTableAt;
        }

        string formatted;

        if (isPaused && pausedRemainingSec.HasValue)
        {
            formatted = FormatSeconds(pausedRemainingSec.Value);
        }
        else if (phase == "left_table")
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long leaveTime = leftTableAt ?? now;
            long overtimeMs = Math.Max(0, now - leaveTime);
            int elapsedSec = (int)(overtimeMs / 1000);

            formatted = FormatSeconds(elapsedSec);
        }
        else if (phase == "idle" || (startedAt == null && breakZeroReachedAt == null))
        {
            formatted = "Ready";
        }
        else if (phase == "break" || phase == "break_ended")
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long elapsedMs = startedAt.HasValue ? Math.Max(0, now - startedAt.Value) : 0;
            int elapsedSec = (int)(elapsedMs / 1000);

            bool isOver = phase == "break_ended" || (startedAt.HasValue && elapsedSec >= durationSec);

            if (isOver)
            {
                if (phase != "break_ended")
                {
                    phase = "break_ended";
                    lock (_lock)
                    {
                        _phase = "break_ended";
                        if (_breakZeroReachedAt == null && startedAt.HasValue)
                        {
                            _breakZeroReachedAt = startedAt.Value + (long)durationSec * 1000;
                        }
                        breakZeroReachedAt = _breakZeroReachedAt;
                    }
                }

                long breakEndMs = breakZeroReachedAt ?? (startedAt.HasValue ? startedAt.Value + (long)durationSec * 1000 : now);
                long overtimeMs = Math.Max(0, now - breakEndMs);
                int postBreakSec = (int)(overtimeMs / 1000);

                formatted = FormatSeconds(postBreakSec);
            }
            else
            {
                int remainingSec = Math.Max(0, durationSec - elapsedSec);
                formatted = FormatSeconds(remainingSec);
            }
        }
        else
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long elapsedMs = Math.Max(0, now - startedAt!.Value);
            int elapsedSec = (int)(elapsedMs / 1000);
            int remainingSec = Math.Max(0, durationSec - elapsedSec);

            formatted = FormatSeconds(remainingSec);
        }

        StateChanged?.Invoke(formatted, phase, participants, isPaused);
    }

    private static string FormatSeconds(int totalSec)
    {
        int mins = totalSec / 60;
        int secs = totalSec % 60;
        if (mins >= 60)
        {
            int hrs = mins / 60;
            mins %= 60;
            return $"{hrs}:{mins:D2}:{secs:D2}";
        }
        return $"{mins:D2}:{secs:D2}";
    }
}
