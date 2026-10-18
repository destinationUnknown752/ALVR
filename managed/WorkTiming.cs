using System;
using System.Diagnostics;

namespace AmanatsuLocationVR
{

    internal sealed class WorkTiming
    {
        long totalTicks, maxTicks;
        int samples;

        public void AddTicks(long ticks)
        {
            ticks = Math.Max(0L, ticks);
            totalTicks += ticks;
            maxTicks = Math.Max(maxTicks, ticks);
            ++samples;
        }

        public string ReadAndReset(string name)
        {
            double factor = 1000.0 / Stopwatch.Frequency;
            string result = name + " " + (samples == 0 ? 0 : totalTicks * factor / samples).ToString("F2")
                + " ms avg / " + (maxTicks * factor).ToString("F2") + " max";
            totalTicks = maxTicks = 0;
            samples = 0;
            return result;
        }
    }
}
