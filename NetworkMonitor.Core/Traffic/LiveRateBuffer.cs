using NetworkMonitor.Models.Charting;

namespace NetworkMonitor.Core.Traffic
{
    // The chart behind the floating mini graph. It fills from app startup whether or not the widget
    // is open, which is what lets the widget open with five minutes already drawn instead of an
    // empty chart, and it costs a fixed ~15 KB to leave running.
    //
    // Idle seconds must read as a flat zero line rather than a hole in the trace, so advancing the
    // ring zeroes every bucket it skips over.
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

                if (firstEpoch < oldestHeld)
                {
                    firstEpoch = oldestHeld;
                }

                List<DateTime> bucketStarts = new List<DateTime>();

                for (long epoch = firstEpoch; epoch <= endEpoch; epoch++)
                {
                    bucketStarts.Add(DateTime.UnixEpoch.AddSeconds(epoch));
                }

                long[] downloadShares = FlushSpread.Distribute(bytesDownloaded, bucketStarts, 1.0, intervalStartUtc, intervalEndUtc);
                long[] uploadShares = FlushSpread.Distribute(bytesUploaded, bucketStarts, 1.0, intervalStartUtc, intervalEndUtc);

                for (int index = 0; index < bucketStarts.Count; index++)
                {
                    Accumulate(firstEpoch + index, downloadShares[index], uploadShares[index]);
                }

            }

        }

        public IReadOnlyList<ChartPoint> Snapshot(DateTime nowUtc)
        {
            List<ChartPoint> points = new List<ChartPoint>();

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

        }

        private void Accumulate(long epoch, long bytesDownloaded, long bytesUploaded)
        {

            if (IsHeld(epoch))
            {
                int slot = Slot(epoch);
                _download[slot] += bytesDownloaded;
                _upload[slot] += bytesUploaded;
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
