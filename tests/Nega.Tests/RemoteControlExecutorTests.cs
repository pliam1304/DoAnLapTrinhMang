using Nega.Agent.Control;
using Nega.Common.Models;

namespace Nega.Tests;

public sealed class RemoteControlExecutorTests
{
    [Fact]
    public async Task ExecuteLockAndUnlock_TogglesStateAndEmitsAck()
    {
        var acks = new List<AckResponsePayload>();
        var executor = new RemoteControlExecutor("sim-may-01", "MAY-01", isSimulated: true, ack =>
        {
            acks.Add(ack);
            return Task.CompletedTask;
        });

        await executor.ExecuteLockCommandAsync(new LockInputPayload { CommandId = "cmd1", Locked = true });
        Assert.True(executor.IsLocked);
        Assert.Single(acks);
        Assert.Equal("LOCK", acks[0].CommandType);

        await executor.ExecuteLockCommandAsync(new LockInputPayload { CommandId = "cmd2", Locked = false });
        Assert.False(executor.IsLocked);
        Assert.Equal(2, acks.Count);
        Assert.Equal("UNLOCK", acks[1].CommandType);
    }

    [Fact]
    public async Task TriggerFailSafeUnlockIfDisconnected_UnlocksLockedWorkstation()
    {
        var executor = new RemoteControlExecutor("sim-may-01", "MAY-01", isSimulated: true, _ => Task.CompletedTask);
        await executor.ExecuteLockCommandAsync(new LockInputPayload { Locked = true });
        Assert.True(executor.IsLocked);

        executor.TriggerFailSafeUnlockIfDisconnected();
        Assert.False(executor.IsLocked);
    }

    [Fact]
    public async Task ExecuteShutdownAndCancel_RespectsDryRunAndEmitsAcks()
    {
        var acks = new List<AckResponsePayload>();
        var executor = new RemoteControlExecutor("sim-may-01", "MAY-01", isSimulated: true, ack =>
        {
            acks.Add(ack);
            return Task.CompletedTask;
        });

        await executor.ExecuteShutdownCommandAsync(new ShutdownCommandPayload
        {
            CommandId = "sd1",
            Action = "shutdown",
            DelaySeconds = 30,
            DryRun = true
        });

        Assert.NotNull(executor.ShutdownRemainingSeconds);
        Assert.InRange(executor.ShutdownRemainingSeconds!.Value, 1, 30);
        Assert.Contains(acks, a => a.CommandType == "SHUTDOWN" && a.CommandId == "sd1");

        await executor.ExecuteShutdownCommandAsync(new ShutdownCommandPayload
        {
            CommandId = "sd1",
            Action = "cancel",
            DryRun = true
        });

        Assert.Null(executor.ShutdownRemainingSeconds);
        Assert.Contains(acks, a => a.CommandType == "CANCEL_SHUTDOWN" && a.CommandId == "sd1");
    }

    [Fact]
    public async Task ExecuteEmergencyAlert_SetsActiveAlertWithoutBlocking()
    {
        var ackTcs = new TaskCompletionSource<AckResponsePayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new RemoteControlExecutor("sim-may-01", "MAY-01", isSimulated: true, ack =>
        {
            if (ack.CommandType == "ALERT_READ")
            {
                ackTcs.TrySetResult(ack);
            }
            return Task.CompletedTask;
        });

        executor.ExecuteEmergencyAlert(new EmergencyAlertPayload
        {
            AlertId = "al1",
            Title = "THÔNG BÁO THI",
            Message = "Còn 5 phút",
            RequireAck = false
        });

        Assert.Equal("THÔNG BÁO THI", executor.ActiveAlertTitle);
        Assert.Equal("Còn 5 phút", executor.ActiveAlertMessage);
        await Task.CompletedTask;
    }
}
