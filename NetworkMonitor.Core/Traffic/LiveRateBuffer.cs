using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Core.Traffic
{
    // The chart behind the floating mini graph. It fills from app startup whether or not the widget
    // is open, which is what lets the widget open with five minutes already drawn instead of an
    // empty chart, and it costs a fixed ~15 KB to leave running.
    //
    // Idle seconds must read as a flat zero line rather than a hole in the trace, so advancing the
    // ring zeroes every bucket it skips over.
    //
    // NOT THREAD-SAFE, deliberately. All three mutators and Snapshot touch _lastEpoch and the arrays
    // with no lock, so every caller must hold one. In this app that is LiveTrafficFeed._gate, which
    // wraps every AddInterval and Snapshot call. This is a public type in Core, so the contract is
    // stated here rather than left to be inferred from the one caller that currently honours it.
    public sealed class LiveRateBuffer
    {
        private readonly long[] _download;
        private readonly long[] _upload;
        private long _lastEpoch = -1;

        public LiveRateBuffer(int capacitySeconds)
        {

            if (capacitySeconds < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacitySeconds));
            }

            _capacity = capacitySeconds;
            _download = new long[capacitySeconds];
            _upload = new long[capacitySeconds];
        }

        private readonly int _capacity;

        public int Capacity => _capacity;

        public void Add(DateTime timestampUtc, long bytesDownloaded, long bytesUploaded)
        {
            long epoch = ToEpochSeconds(timestampUtc);

            Advance(epoch);
            Accumulate(epoch, bytesDownloaded, bytesUploaded);
        }

        // A flush drains everything accumulated since the previous drain, so its bytes belong to the
        // whole interval rather than to the instant the drain happened. Charging them to one bucket
        // draws a spike above the physical line rate at any traffic interval above one second.
        public void AddInterval(DateTime intervalStartUtc, DateTime intervalEndUtc, long bytesDownloaded, long bytesUploaded)
        {

            if (intervalEndUtc <= intervalStartUtc)
            {
                Add(intervalEndUtc, bytesDownloaded, bytesUploaded);
            }
            else
            {
                long endEpoch = ToEpochSeconds(intervalEndUtc);

                Advance(endEpoch);

                long firstEpoch = ToEpochSeconds(intervalStartUtc);
                long oldestHeld = _lastEpoch - _capacity + 1;
                DateTime effectiveStartUtc = intervalStartUtc;
                long retainedDownloaded = bytesDownloaded;
                long retainedUploaded = bytesUploaded;

                // A gap longer than the window would otherwise have all its bytes normalised over the
                // ~300 seconds still held, drawing hours of traffic at tens of times the real rate and
                // dragging the whole axis up with it. Only the share belonging to the seconds we can
                // still show is kept; the rest belongs to buckets that have already scrolled off.
                if (firstEpoch < oldestHeld)
                {
                    firstEpoch = oldestHeld;
                    effectiveStartUtc = DateTime.UnixEpoch.AddSeconds(firstEpoch);

                    double intervalSeconds = (intervalEndUtc - intervalStartUtc).TotalSeconds;
                    double retainedSeconds = (intervalEndUtc - effectiveStartUtc).TotalSeconds;

                    if (retainedSeconds <= 0.0 || intervalSeconds <= 0.0)
                    {
                        retainedDownloaded = 0;
                        retainedUploaded = 0;
                    }
                    else
                    {
                        double keptShare = retainedSeconds / intervalSeconds;
                        retainedDownloaded = (long)(bytesDownloaded * keptShare);
                        retainedUploaded = (long)(bytesUploaded * keptShare);
                    }

                }

                int bucketCount = (int)Math.Max(0L, endEpoch - firstEpoch + 1L);
                List<DateTime> bucketStarts = new List<DateTime>(bucketCount);

                for (long epoch = firstEpoch; epoch <= endEpoch; epoch++)
                {
                    bucketStarts.Add(DateTime.UnixEpoch.AddSeconds(epoch));
                }

                long[] downloadShares = FlushSpread.Distribute(retainedDownloaded, bucketStarts, 1.0, effectiveStartUtc, intervalEndUtc);
                long[] uploadShares = FlushSpread.Distribute(retainedUploaded, bucketStarts, 1.0, effectiveStartUtc, intervalEndUtc);

                for (int index = 0; index < bucketStarts.Count; index++)
                {
                    Accumulate(firstEpoch + index, downloadShares[index], uploadShares[index]);
                }

            }

        }

        // The list is sized up front rather than grown: a snapshot always fills the whole ring, so an
        // unsized list reallocates its way up to capacity — nine intermediate arrays thrown away —
        // every time, twice per flush across the two buffers.
        //
        // A caller-supplied buffer would remove even this one, but the returned list outlives the
        // call: the widget holds the previous snapshot while the chart is still drawing from it, so a
        // shared buffer would be rewritten underneath a live trace.
        public IReadOnlyList<ChartPoint> Snapshot(DateTime nowUtc)
        {
            List<ChartPoint> points = new List<ChartPoint>(_capacity);

            if (_lastEpoch >= 0)
            {
                long nowEpoch = ToEpochSeconds(nowUtc);
                long endEpoch = nowEpoch > _lastEpoch ? nowEpoch : _lastEpoch;
                long startEpoch = endEpoch - _capacity + 1;

                for (long epoch = startEpoch; epoch <= endEpoch; epoch++)
                {
                    long download = 0;
                    long upload = 0;

                    if (IsHeld(epoch))
                    {
                        int slot = Slot(epoch);
                        download = _download[slot];
                        upload = _upload[slot];
                    }

                    points.Add(new ChartPoint(DateTime.UnixEpoch.AddSeconds(epoch), upload, download));
                }

            }

            return points;
        }

        public void Clear()
        {
            Array.Clear(_download);
            Array.Clear(_upload);
            _lastEpoch = -1;
        }

        private void Advance(long epoch)
        {

            if (_lastEpoch < 0)
            {
                _lastEpoch = epoch;
                int slot = Slot(epoch);
                _download[slot] = 0;
                _upload[slot] = 0;
            }
            else if (epoch > _lastEpoch)
            {
                long first = _lastEpoch + 1;

                if (epoch - first >= _capacity)
                {
                    first = epoch - _capacity + 1;
                }

                for (long skipped = first; skipped <= epoch; skipped++)
                {
                    int slot = Slot(skipped);
                    _download[slot] = 0;
                    _upload[slot] = 0;
                }

                _lastEpoch = epoch;
            }
            else if (epoch < _lastEpoch && epoch > _lastEpoch - _capacity)
            {
                // Time moved backwards inside the window — an NTP correction after RTC drift, a VM
                // resume, a user setting the clock. This is the case that corrupted: the bucket is
                // still held, so Accumulate stacked new traffic on top of pre-jump bytes, nothing
                // was ever zeroed until wall-clock caught up, and Snapshot pinned the right edge in
                // the future meanwhile. A discontinuity is not a gap, so the trace restarts rather
                // than zero-filling backwards.
                //
                // A step older than the whole window is left alone: IsHeld already drops it, which
                // is the long-standing behaviour for an out-of-order sample and is pinned by
                // LiveRateBufferTests.SamplesOlderThanTheWindowAreDropped.
                Clear();

                _lastEpoch = epoch;
                int slot = Slot(epoch);
                _download[slot] = 0;
                _upload[slot] = 0;
            }

        }

        private void Accumulate(long epoch, long bytesDownloaded, long bytesUploaded)
        {

            if (IsHeld(epoch))
            {
                int slot = Slot(epoch);

                // Negatives are dropped per counter, matching FlushSpread.Distribute, which returns
                // zeros for a negative total. See the note there for why a negative can only be a
                // broken input rather than a measurement.
                if (bytesDownloaded > 0)
                {
                    _download[slot] += bytesDownloaded;
                }

                if (bytesUploaded > 0)
                {
                    _upload[slot] += bytesUploaded;
                }

            }

        }

        private bool IsHeld(long epoch)
        {
            bool held = epoch <= _lastEpoch && epoch > _lastEpoch - _capacity;

            return held;
        }

        private int Slot(long epoch)
        {
            int slot = (int)(epoch % _capacity);

            return slot;
        }

        private static long ToEpochSeconds(DateTime timestampUtc)
        {
            long epoch = (long)(timestampUtc - DateTime.UnixEpoch).TotalSeconds;

            return epoch;
        }
    }
}
