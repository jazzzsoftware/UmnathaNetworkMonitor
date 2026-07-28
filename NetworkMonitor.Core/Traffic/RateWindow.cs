namespace NetworkMonitor.Core.Traffic
{
    // A short rolling window of per-flush byte counts. It keeps its own running total so a live
    // tick costs one add and one subtract per group on screen, rather than a pass over the queue.
    public sealed class RateWindow
    {
        private readonly Queue<long> _samples = new Queue<long>();

        public long Total
        {
            get;
            private set;
        }

        public int Count => _samples.Count;

        public double Average => _samples.Count == 0 ? 0.0 : (double)Total / _samples.Count;

        public void Add(long bytes, int maxSamples)
        {
            _samples.Enqueue(bytes);
            Total += bytes;

            while (_samples.Count > maxSamples)
            {
                Total -= _samples.Dequeue();
            }

        }
    }
}
