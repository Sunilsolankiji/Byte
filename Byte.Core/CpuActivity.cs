using System;

namespace Byte.Core;

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
public enum CpuReaction { Busy, Relax }

public sealed class CpuActivity
{
    private CpuTimes? previous;
    private int highSamples, lowSamples;
    private bool highLoad;
    private double nextReaction;
    public double? UsagePercent { get; private set; }
    public bool HighLoad => highLoad;

    public void Reset(bool preserveCooldown = false)
    {
        previous = null;
        UsagePercent = null;
        highSamples = lowSamples = 0;
        highLoad = false;
        if (!preserveCooldown) nextReaction = 0;
    }

    public CpuReaction? Sample(CpuTimes current, double now)
    {
        var last = previous;
        previous = current;
        if (!last.HasValue) return null;
        var before = last.Value;
        if (current.Idle < before.Idle || current.Kernel < before.Kernel || current.User < before.User)
        {
            Reset(preserveCooldown: true);
            previous = current;
            return null;
        }
        // Windows kernel time includes idle time. All values are cumulative 100-ns ticks.
        var total = (double)(current.Kernel - before.Kernel) + (current.User - before.User);
        var idle = (double)(current.Idle - before.Idle);
        if (total <= 0 || idle > total)
        {
            UsagePercent = null;
            highSamples = lowSamples = 0;
            return null;
        }
        UsagePercent = (total - idle) / total * 100;
        highSamples = UsagePercent >= 75 ? Math.Min(3, highSamples + 1) : 0;
        lowSamples = UsagePercent <= 40 ? Math.Min(3, lowSamples + 1) : 0;
        if (!highLoad && highSamples >= 3)
        {
            highLoad = true;
            highSamples = 0;
            if (now >= nextReaction)
            {
                nextReaction = now + 60;
                return CpuReaction.Busy;
            }
        }
        else if (highLoad && lowSamples >= 3)
        {
            highLoad = false;
            lowSamples = 0;
            if (now >= nextReaction)
            {
                nextReaction = now + 60;
                return CpuReaction.Relax;
            }
        }
        return null;
    }
}
