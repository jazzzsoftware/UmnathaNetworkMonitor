using NetworkMonitor.Models.Traffic;

namespace NetworkMonitor.Core.Traffic
{
    // The two filters below are the entire reason the widget's numbers agree with the Internet and
    // Local tabs. They lived inline in LiveTrafficFeed.OnFlushed — in Services, where the test project
    // cannot reach them — with comments explaining exactly why each mattered and nothing pinning
    // either. A silent drift between the widget and the tab is the kind of defect a user reports as
    // "the numbers don't match" months later.
    public static class WidgetTrafficTotals
    {
        private const string SystemProcessName = "System";

        // The Internet tab hides System, so including it here would put the widget and the tab
        // permanently out of step.
        public static TrafficTotals Wan(IEnumerable<TrafficEntry> entries)
        {
            long download = 0;
            long upload = 0;

            foreach (TrafficEntry entry in entries)
            {

                if (entry.ProcessName != SystemProcessName)
                {
                    download += entry.BytesDownloaded;
                    upload += entry.BytesUploaded;
                }

            }

            TrafficTotals totals = new TrafficTotals(download, upload);

            return totals;
        }

        // The Local tab's chart excludes discovery traffic and so must this one. mDNS, SSDP, NetBIOS
        // and DHCP tick over on every device on the segment, so counting them drew a dense sawtooth in
        // the widget beside a near-flat line on the tab — the same two minutes of the same network.
        public static TrafficTotals Lan(IEnumerable<LocalTrafficDelta> deltas)
        {
            long download = 0;
            long upload = 0;

            foreach (LocalTrafficDelta delta in deltas)
            {
                FlowClassification classification = LocalFlowClassifier.Classify(delta.Protocol, delta.RemotePort);

                if (classification.Category == FlowCategory.Data)
                {
                    download += delta.BytesDownloaded;
                    upload += delta.BytesUploaded;
                }

            }

            TrafficTotals totals = new TrafficTotals(download, upload);

            return totals;
        }
    }
}
