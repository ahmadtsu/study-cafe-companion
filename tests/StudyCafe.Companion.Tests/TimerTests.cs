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
}
