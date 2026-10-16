using StudyCafe.Companion.Core;
using StudyCafe.Companion.Taskbar;
using System.Windows;

namespace StudyCafe.Companion.Tests;

public class TimerTests
{
    [Fact]
    public void TimerEngine_Formats_MinutesAndSeconds()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // 25 minutes ago started 30 seconds ago -> 24:30 remaining
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.HandlePomoStart(25, now - 30_000);

        Assert.Equal("24:30", resultTime);
        Assert.Equal("work", resultPhase);
    }

    [Fact]
    public void TimerEngine_Formats_HoursMinutesSeconds()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;

        engine.StateChanged += (time, _, _, _) => resultTime = time;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // 90 minute block started 0 seconds ago -> 1:30:00
        engine.ApplyRoomState(new RoomStateModel
        {
            Phase = "work",
            DurationSec = 90 * 60,
            StartedAt = now,
            IsPaused = false,
        });

        Assert.Equal("1:30:00", resultTime);
    }

    [Fact]
    public void TimerEngine_Handles_PauseState()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        bool isPausedResult = false;

        engine.StateChanged += (time, _, _, isPaused) =>
        {
            resultTime = time;
            isPausedResult = isPaused;
        };

        engine.HandleTogglePause(true, 15 * 60 + 45, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        Assert.True(isPausedResult);
        Assert.Equal("15:45", resultTime);
    }

    [Fact]
    public void TimerEngine_Formats_BreakCountdown()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // 5 minute break started 30 seconds ago -> 04:30 remaining
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.HandleBreakStart(5, now - 30_000);

        Assert.Equal("04:30", resultTime);
        Assert.Equal("break", resultPhase);
    }

    [Fact]
    public void TimerEngine_CountsTimeSpentOutsideBreak_WhenBreakExpires()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // 5 minute break started 5 minutes and 30 seconds ago -> 30 seconds spent outside break
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.HandleBreakStart(5, now - (5 * 60 + 30) * 1000);

        Assert.Equal("00:30", resultTime);
        Assert.Equal("break_ended", resultPhase);
    }

    [Fact]
    public void TimerEngine_CountsTimeSpentOutsideBreak_FromBreakZeroReachedAt()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // RoomState synced with break_ended and breakZeroReachedAt set 90 seconds ago -> 01:30
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.ApplyRoomState(new RoomStateModel
        {
            Phase = "break_ended",
            DurationSec = 5 * 60,
            StartedAt = now - 390_000,
            BreakZeroReachedAt = now - 90_000,
            IsPaused = false,
        });

        Assert.Equal("01:30", resultTime);
        Assert.Equal("break_ended", resultPhase);
    }

    [Fact]
    public void TimerEngine_CountsHours_WhenLongTimeOutsideBreak()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // Break ended 1 hour, 15 minutes, 10 seconds ago -> 1:15:10
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long breakEndedMs = now - (1 * 3600 + 15 * 60 + 10) * 1000;
        engine.HandleBreakEnded(breakEndedMs);

        Assert.Equal("1:15:10", resultTime);
        Assert.Equal("break_ended", resultPhase);
    }

    [Fact]
    public void TimerEngine_TransitionsBackToWork_WhenPomoStartsAfterBreak()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // First on break which has ended
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.HandleBreakStart(5, now - 400_000);
        Assert.Equal("break_ended", resultPhase);

        // Then new focus starts
        engine.HandlePomoStart(25, now - 10_000);
        Assert.Equal("24:50", resultTime);
        Assert.Equal("work", resultPhase);
    }

    [Fact]
    public void TimerEngine_CountsTimeSpentAway_WhenUserLeavesTable()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // User left table 45 seconds ago -> counts 00:45 with left_table phase
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.HandleLeaveTable(now - 45_000);

        Assert.Equal("00:45", resultTime);
        Assert.Equal("left_table", resultPhase);
    }

    [Fact]
    public void TimerEngine_AppliesRoomState_WithLeftTablePhase()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.ApplyRoomState(new RoomStateModel
        {
            Phase = "left_table",
            LeftTableAt = now - 125_000,
        });

        Assert.Equal("02:05", resultTime);
        Assert.Equal("left_table", resultPhase);
    }

    [Fact]
    public void TaskbarBounds_ReturnsNull_WhenTaskbarVertical()
    {
        var taskbarRect = new Rect(0, 0, 48, 1080); // vertical
        var layout = new TaskbarLayout(new Rect(0, 0, 48, 400), Array.Empty<Rect>());

        var bounds = TaskbarBounds.Calculate(nint.Zero, taskbarRect, layout, 1.0, 80);

        Assert.Null(bounds);
    }

    [Fact]
    public void TaskbarBounds_Calculates_ValidWidgetBounds()
    {
        var taskbarRect = new Rect(0, 1032, 1920, 48); // horizontal
        var cluster = new Rect(700, 1032, 400, 48);
        var trayObstacle = new Rect(1700, 1032, 220, 48);
        var layout = new TaskbarLayout(cluster, new[] { trayObstacle });

        // Note: ProjectSpan uses user32 ScreenToClient on the taskbar HWND.
        // For unit test where hwnd is 0, ScreenToClient fails safely and retains coordinates.
        var bounds = TaskbarBounds.Calculate(nint.Zero, taskbarRect, layout, 1.0, 90);

        Assert.NotNull(bounds);
        Assert.True(bounds.Value.Width > 0);
        Assert.True(bounds.Value.Height > 0);
    }

    [Fact]
    public void TimerEngine_CountsUpInRed_WhenWorkSessionExpires()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // 25 min work session started 25 min 15 sec ago -> 15 sec overtime
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.HandlePomoStart(25, now - (25 * 60 + 15) * 1000);

        Assert.Equal("00:15", resultTime);
        Assert.Equal("break_ended", resultPhase);
    }

    [Fact]
    public void TimerEngine_KeepsRedTimerRunning_WhenReset()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        // Reset called: timer must keep running in red, not stop
        engine.HandleReset();

        Assert.NotNull(resultTime);
        Assert.Equal("break_ended", resultPhase);
    }

    [Fact]
    public void TimerEngine_CountsOvertime_FromWorkZeroReachedAt()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;
        string? resultPhase = null;

        engine.StateChanged += (time, phase, _, _) =>
        {
            resultTime = time;
            resultPhase = phase;
        };

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        engine.ApplyRoomState(new RoomStateModel
        {
            Phase = "work",
            DurationSec = 25 * 60,
            StartedAt = now - 1600_000,
            WorkZeroReachedAt = now - 100_000, // expired 100s ago -> 01:40
            IsPaused = false,
        });

        Assert.Equal("01:40", resultTime);
        Assert.Equal("break_ended", resultPhase);
    }

    [Fact]
    public void TimerEngine_DoesNotResetBreakEndedTimer_OnNullBreakZeroReachedAtSync()
    {
        var engine = new TimerCountdownEngine();
        string? resultTime = null;

        engine.StateChanged += (time, _, _, _) => resultTime = time;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Break ended 45 seconds ago
        engine.ApplyRoomState(new RoomStateModel
        {
            Phase = "break_ended",
            DurationSec = 5 * 60,
            StartedAt = now - 345_000,
            BreakZeroReachedAt = now - 45_000,
        });
        Assert.Equal("00:45", resultTime);

        // Another sync packet arrives where BreakZeroReachedAt is null
        engine.ApplyRoomState(new RoomStateModel
        {
            Phase = "break_ended",
            DurationSec = 5 * 60,
            StartedAt = now - 345_000,
            BreakZeroReachedAt = null,
        });
        // Should retain the original 45 seconds rather than resetting to 00:00
        Assert.Equal("00:45", resultTime);
    }
}
