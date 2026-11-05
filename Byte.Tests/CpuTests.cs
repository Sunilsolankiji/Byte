using System;
using System.Linq;
using System.Text.Json;
using Byte.Core;

internal static class CpuTests
{
    public static void Run(Action<bool, string> check, Display display)
    {
        var cpu = new CpuActivity();
        ulong idle = 0, kernel = 0, user = 0;
        double now = 0;
        CpuReaction? Sample(int percent, double seconds = 2)
        {
            idle += (ulong)(100 - percent);
            kernel += 60;
            user += 40;
            now += seconds;
            return cpu.Sample(new(idle, kernel, user), now);
        }
        check(cpu.Sample(new(0, 0, 0), now) is null && cpu.UsagePercent is null,
            "CPU first sample establishes a baseline, not a fake zero reading");
        Sample(50);
        check(cpu.UsagePercent == 50, "CPU calculation includes kernel idle time exactly once");
        Sample(80); Sample(20); Sample(80); Sample(80);
        check(!cpu.HighLoad, "short CPU spikes do not cause a busy reaction");
        check(Sample(80) == CpuReaction.Busy && cpu.HighLoad,
            "three consecutive high samples trigger sustained CPU reaction");
        check(Enumerable.Range(0, 20).All(_ => Sample(95) is null),
            "steady high load does not produce repetitive reactions");
        Sample(40); Sample(40);
        check(cpu.HighLoad, "CPU recovery waits for three samples at or below 40 percent");
        check(Sample(40, 20) == CpuReaction.Relax && !cpu.HighLoad,
            "sustained low load relaxes after reaction cooldown");
        Sample(85); Sample(85);
        check(Sample(85) is null && cpu.HighLoad, "CPU changes cannot react more than once per minute");
        cpu.Reset();
        check(cpu.UsagePercent is null && !cpu.HighLoad && cpu.Sample(new(50, 80, 40), now) is null,
            "CPU opt-out/suspend reset discards history and primes a new baseline");
        check(cpu.Sample(new(51, 80, 40), now) is null && cpu.UsagePercent is null,
            "CPU invalid zero-total counter sample is not reported as healthy load");
        cpu.Sample(new(0, 0, 0), now);
        check(cpu.UsagePercent is null, "counter rollback resets CPU sampling instead of underflowing");
        cpu.Sample(new(200, 40, 40), now);
        check(cpu.UsagePercent is null, "invalid CPU idle/total relationship is rejected");
        cpu.Reset();
        cpu.Sample(new(0, 0, 0), now);
        Sample(75); Sample(75);
        check(Sample(75) == CpuReaction.Busy, "high CPU threshold includes exactly 75 percent");
        cpu.Reset(preserveCooldown: true);
        cpu.Sample(new(idle, kernel, user), now);
        Sample(75); Sample(75);
        check(Sample(75) is null, "busy/hide sampling reset preserves CPU reaction cooldown");

        var settings = new Settings();
        var bot = new Companion(new[] { display }, settings, 20);
        check(!settings.CpuReactions && !bot.ReactToCpu(CpuReaction.Busy),
            "CPU reads/reactions are opt-in for new and existing settings");
        var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings { CpuReactions = true }))!;
        check(restored.CpuReactions && !JsonSerializer.Deserialize<Settings>("{\"Size\":1}")!.CpuReactions,
            "CPU opt-in persists while legacy settings remain disabled");
        settings.CpuReactions = true;
        var revision = bot.Memory.Revision;
        var signals = bot.SignalVersion;
        var speech = 0;
        bot.Speech += _ => speech++;
        foreach (var kind in Enum.GetValues<CharacterKind>())
        {
            bot.Kind = kind;
            bot.Act(Mood.Idle);
            check(bot.ReactToCpu(CpuReaction.Busy) && bot.Mood == Mood.CpuBusy,
                kind + " can display busy CPU pose without changing monitors");
            bot.EndCpuReaction();
            check(bot.Mood == Mood.Idle && bot.ReactToCpu(CpuReaction.Relax) && bot.Mood == Mood.CpuRelax,
                kind + " can quietly relax and cancel CPU poses");
        }
        check(bot.Memory.Revision == revision && bot.SignalVersion == signals && speech == 0,
            "CPU load is never written to memory, emitted to friends or spoken aloud");
        bot.EndCpuReaction();
        bot.Paused = true;
        check(!bot.ReactToCpu(CpuReaction.Busy), "paused companion ignores CPU reactions");
        bot.Paused = false;
        bot.ReducedMotion = true;
        check(!bot.ReactToCpu(CpuReaction.Busy), "reduced motion suppresses CPU animation");
        bot.ReducedMotion = false;
        settings.Activity = ActivityLevel.Quiet;
        check(!bot.ReactToCpu(CpuReaction.Busy), "Quiet activity level suppresses CPU reactions");
        settings.Activity = ActivityLevel.Balanced;
        bot.StartFocus(1);
        check(!bot.ReactToCpu(CpuReaction.Busy), "focus timer has priority over CPU reactions");
        bot.StopFocus(); bot.Act(Mood.Idle);
        bot.StartDrag();
        check(!bot.ReactToCpu(CpuReaction.Busy) && bot.Mood == Mood.Drag, "CPU reaction never takes over dragging");
        bot.CancelDrag();
        bot.ApproachFriend(bot.Feet.X);
        check(!bot.ReactToCpu(CpuReaction.Busy) && bot.IsSocial, "CPU reaction never interrupts friend choreography");
        bot.EndSocial();
        bot.Act(Mood.Sleep);
        check(!bot.ReactToCpu(CpuReaction.Busy), "CPU load never wakes a sleeping character");
        bot.Act(Mood.Idle);
        bot.ReactToCpu(CpuReaction.Busy);
        bot.Tick(.02, default, true);
        check(bot.Mood == Mood.Sit, "busy pointer activity immediately backs off a CPU reaction");
        bot.Act(Mood.Celebrate);
        bot.EndCpuReaction();
        check(bot.Mood == Mood.Celebrate, "canceling CPU reactions preserves unrelated user interactions");
        for (var i = 0; i < 17; i++) bot.Tick(.25, default, false);
        check(bot.Mood == Mood.Idle, "explicit poses retain their normal bounded duration");
        bot.ReactToCpu(CpuReaction.Busy);
        for (var i = 0; i < 17; i++) bot.Tick(.25, default, false);
        check(bot.Mood == Mood.Idle, "CPU busy pose ends automatically after four seconds");
        var second = new Display("cpu-second", new RectD(1920, 0, 1920, 1080), new RectD(1920, 0, 1920, 1040), 1, false);
        var traveler = new Companion(new[] { display, second }, settings);
        traveler.Summon(new PointD(display.Work.Left + 200, display.Work.Bottom));
        traveler.JumpTo(second);
        check(!traveler.ReactToCpu(CpuReaction.Busy), "CPU load cannot cancel edge approach for a monitor jump");
    }
}
