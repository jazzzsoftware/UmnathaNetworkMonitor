using System;
using System.Collections.Generic;

namespace NetworkMonitor.Core.Traffic
{
    public static class FlushSpread
    {
        // A flush drains everything the collector accumulated since the previous drain, so its bytes
        // belong to the whole interval [intervalStart, intervalEnd) — not to the instant the drain
        // happened. Charging them to a single bucket leaves a hole wherever a drain slipped past a
        // bucket boundary and doubles the bucket that caught up, which is what produced peaks above
        // the physical line rate while the sustained rate was correct.
        //
        // Returns one byte figure per bucket, summing to exactly totalBytes so no traffic is lost.
        //
        // A negative totalBytes returns all zeros rather than distributing it, and LiveRateBuffer
        // matches that by refusing to accumulate one. ETW counters are unsigned and accumulate, so a
        // negative can only mean a counter reset or a wrapped subtraction upstream — a broken input,
        // not a real measurement. Spreading it would drag buckets below zero and pull the whole axis
        // with them. Previously the two entry points disagreed: this returned zeros while
        // LiveRateBuffer.Accumulate happily added a negative, so the outcome depended on which path
        // ran. Both now drop it.
        public static long[] Distribute(
            long totalBytes,
            IReadOnlyList<DateTime> bucketStartsUtc,
            double bucketSeconds,
            DateTime intervalStartUtc,
            DateTime intervalEndUtc)
        {
            long[] allocated = new long[bucketStartsUtc.Count];

            if (bucketStartsUtc.Count > 0 && totalBytes > 0)
            {
                double[] overlaps = new double[bucketStartsUtc.Count];
                double totalOverlap = 0.0;

                if (bucketSeconds > 0.0 && intervalEndUtc > intervalStartUtc)
                {

                    for (int index = 0; index < bucketStartsUtc.Count; index++)
                    {
                        DateTime bucketStart = bucketStartsUtc[index];
                        DateTime bucketEnd = bucketStart.AddSeconds(bucketSeconds);
                        DateTime overlapStart = bucketStart > intervalStartUtc ? bucketStart : intervalStartUtc;
                        DateTime overlapEnd = bucketEnd < intervalEndUtc ? bucketEnd : intervalEndUtc;
                        double overlap = (overlapEnd - overlapStart).TotalSeconds;

                        if (overlap > 0.0)
                        {
                            overlaps[index] = overlap;
                            totalOverlap += overlap;
                        }

                    }

                }

                if (totalOverlap <= 0.0)
                {
                    // Degenerate interval, or one that falls entirely outside the window: the newest
                    // bucket is the only defensible home for the bytes.
                    allocated[allocated.Length - 1] = totalBytes;
                }
                else
                {
                    long assigned = 0;
                    int largestIndex = 0;

                    for (int index = 0; index < overlaps.Length; index++)
                    {
                        long share = (long)(totalBytes * (overlaps[index] / totalOverlap));
                        allocated[index] = share;
                        assigned += share;

                        if (overlaps[index] > overlaps[largestIndex])
                        {
                            largestIndex = index;
                        }

                    }

                    // Integer truncation loses a few bytes; give them to the bucket with the most
                    // overlap so the total is preserved exactly.
                    allocated[largestIndex] += totalBytes - assigned;
                }

            }

            return allocated;
        }
    }
}
