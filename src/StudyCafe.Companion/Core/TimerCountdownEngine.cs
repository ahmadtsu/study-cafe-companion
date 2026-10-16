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
    private long? _workZeroReachedAt;
    private long? _leftTableAt;
    private long _nonFocusSince = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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

            if (state.BreakZeroReachedAt.HasValue)
            {
                _breakZeroReachedAt = state.BreakZeroReachedAt.Value;
            }

            if (state.WorkZeroReachedAt.HasValue)
            {
                _workZeroReachedAt = state.WorkZeroReachedAt.Value;
            }

            if (state.LeftTableAt.HasValue)
            {
                _leftTableAt = state.LeftTableAt.Value;
            }

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
                    _breakZeroReachedAt = _nonFocusSince;
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
            _workZeroReachedAt = null;
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
            _workZeroReachedAt = null;
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
                    _breakZeroReachedAt = _nonFocusSince;
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
            _leftTableAt = leftTableAt ?? _leftTableAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
            _workZeroReachedAt = null;
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
        long? workZeroReachedAt;
        long? leftTableAt;
        long nonFocusSince;

        lock (_lock)
        {
            phase = _phase;
            durationSec = _durationSec;
            startedAt = _startedAt;
            isPaused = _isPaused;
            pausedRemainingSec = _pausedRemainingSec;
            participants = _participants;
            breakZeroReachedAt = _breakZeroReachedAt;
            workZeroReachedAt = _workZeroReachedAt;
            leftTableAt = _leftTableAt;
            nonFocusSince = _nonFocusSince;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string formatted;

        if (isPaused && pausedRemainingSec.HasValue)
        {
            formatted = FormatSeconds(pausedRemainingSec.Value);
        }
        else if (phase == "left_table")
        {
            long leaveTime = leftTableAt ?? nonFocusSince;
            long overtimeMs = Math.Max(0, now - leaveTime);
            int elapsedSec = (int)(overtimeMs / 1000);

            formatted = FormatSeconds(elapsedSec);
        }
        else if (phase == "break" || phase == "break_ended")
        {
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

                long breakEndMs = breakZeroReachedAt ?? (startedAt.HasValue ? startedAt.Value + (long)durationSec * 1000 : nonFocusSince);
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
        else if (phase == "work" && startedAt.HasValue)
        {
            long elapsedMs = Math.Max(0, now - startedAt.Value);
            int elapsedSec = (int)(elapsedMs / 1000);

            if (elapsedSec >= durationSec)
            {
                // Work session completed! Immediately switch to red timer counting elapsed overtime
                phase = "break_ended";
                lock (_lock)
                {
                    _phase = "break_ended";
                    _breakZeroReachedAt ??= workZeroReachedAt ?? (startedAt.Value + (long)durationSec * 1000);
                    breakZeroReachedAt = _breakZeroReachedAt;
                }

                long workEndMs = breakZeroReachedAt ?? (startedAt.Value + (long)durationSec * 1000);
                long overtimeMs = Math.Max(0, now - workEndMs);
                int postWorkSec = (int)(overtimeMs / 1000);

                formatted = FormatSeconds(postWorkSec);
            }
            else
            {
                int remainingSec = Math.Max(0, durationSec - elapsedSec);
                formatted = FormatSeconds(remainingSec);
            }
        }
        else
        {
            // Idle / ready / no active countdown:
            // Red timer is ALWAYS running so user feels regret if they didn't run a timer
            long idleStart = breakZeroReachedAt ?? leftTableAt ?? nonFocusSince;
            long elapsedMs = Math.Max(0, now - idleStart);
            int elapsedSec = (int)(elapsedMs / 1000);

            phase = "break_ended";
            formatted = FormatSeconds(elapsedSec);
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
