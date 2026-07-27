using NetworkMonitor.Models.Traffic;

namespace NetworkMonitor.Core.Traffic
{
    public static class LocalTrafficGrouper
    {
        public static IReadOnlyList<LocalTrafficGroupRow> Build(IReadOnlyList<LocalFlowMinute> minutes, IReadOnlyDictionary<string, string> namesByIp, LocalLens lens)
        {
            Dictionary<string, GroupAccumulator> foreground = new Dictionary<string, GroupAccumulator>();
            GroupAccumulator background = new GroupAccumulator("__background", "background", null);

            foreach (LocalFlowMinute minute in minutes)
            {
                FlowClassification classification = LocalFlowClassifier.Classify(minute.Protocol, minute.RemotePort);

                if (classification.Category == FlowCategory.Discovery)
                {

                    // Discovery chatter from an address that isn't a known device is dropped
                    // outright — it is broadcast-adjacent noise from peers we can't name, and
                    // showing it as a bare IP row buried the real traffic.
                    if (namesByIp.TryGetValue(minute.RemoteIp, out string? deviceName))
                    {
                        background.Add(minute.RemoteIp, deviceName, minute.RemoteIp, minute.BytesUploaded, minute.BytesDownloaded, null);
                    }

                }
                else
                {
                    AddForeground(foreground, minute, classification.ServiceTag, namesByIp, lens);
                }

            }

            List<LocalTrafficGroupRow> groups = new List<LocalTrafficGroupRow>();
            long totalUpload = 0;
            long totalDownload = 0;
            List<LocalTrafficGroupRow> normals = new List<LocalTrafficGroupRow>();

            foreach (KeyValuePair<string, GroupAccumulator> entry in foreground)
            {
                LocalTrafficGroupRow row = entry.Value.ToRow(GroupKind.Normal);

                normals.Add(row);
                totalUpload += row.BytesUploaded;
                totalDownload += row.BytesDownloaded;
            }

            normals.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

            string allLabel = lens == LocalLens.ByApp ? "All Apps" : "All Devices";
            LocalTrafficGroupRow allRow = new LocalTrafficGroupRow(null, allLabel, null, totalUpload, totalDownload, Array.Empty<LocalTrafficLeafRow>(), GroupKind.All, null);
            groups.Add(allRow);
            groups.AddRange(normals);

            if (background.HasAny)
            {
                groups.Add(background.ToBackgroundRow());
            }

            return groups;
        }

        private static void AddForeground(Dictionary<string, GroupAccumulator> foreground, LocalFlowMinute minute, string? serviceTag, IReadOnlyDictionary<string, string> namesByIp, LocalLens lens)
        {
            string groupKey;
            string groupName;
            string? groupSub;
            string childKey;
            string childName;
            string? childSub;

            if (lens == LocalLens.ByApp)
            {
                groupKey = minute.ProcessName;
                groupName = minute.ProcessName;
                groupSub = null;
                childKey = minute.RemoteIp;
                childName = LocalTrafficNameResolver.Resolve(minute.RemoteIp, namesByIp);
                childSub = minute.RemoteIp;
            }
            else
            {
                groupKey = minute.RemoteIp;
                groupName = LocalTrafficNameResolver.Resolve(minute.RemoteIp, namesByIp);
                groupSub = minute.RemoteIp;
                childKey = minute.ProcessName;
                childName = minute.ProcessName;
                childSub = null;
            }

            if (!foreground.TryGetValue(groupKey, out GroupAccumulator? accumulator))
            {
                accumulator = new GroupAccumulator(groupKey, groupName, groupSub);
                foreground[groupKey] = accumulator;
            }

            accumulator.Add(childKey, childName, childSub, minute.BytesUploaded, minute.BytesDownloaded, serviceTag);
        }

        private sealed class GroupAccumulator
        {
            private readonly string _key;
            private readonly string _name;
            private readonly string? _sub;
            private readonly Dictionary<string, LeafAccumulator> _children = new Dictionary<string, LeafAccumulator>();

            public GroupAccumulator(string key, string name, string? sub)
            {
                _key = key;
                _name = name;
                _sub = sub;
            }

            public bool HasAny => _children.Count > 0;

            public void Add(string childKey, string childName, string? childSub, long upload, long download, string? serviceTag)
            {

                if (!_children.TryGetValue(childKey, out LeafAccumulator? leaf))
                {
                    leaf = new LeafAccumulator(childKey, childName, childSub);
                    _children[childKey] = leaf;
                }

                leaf.Add(upload, download, serviceTag);
            }

            public LocalTrafficGroupRow ToRow(GroupKind kind)
            {
                List<LocalTrafficLeafRow> leaves = new List<LocalTrafficLeafRow>();
                long upload = 0;
                long download = 0;

                foreach (LeafAccumulator leaf in _children.Values)
                {
                    LocalTrafficLeafRow row = leaf.ToRow();

                    leaves.Add(row);
                    upload += row.BytesUploaded;
                    download += row.BytesDownloaded;
                }

                leaves.Sort((left, right) => right.TotalBytes.CompareTo(left.TotalBytes));

                // Taken after the sort so the group is labelled with the service that actually
                // moved the bytes, not whichever child the dictionary happened to yield first.
                string? groupTag = null;

                foreach (LocalTrafficLeafRow leaf in leaves)
                {

                    if (leaf.ServiceTag is not null)
                    {
                        groupTag = leaf.ServiceTag;

                        break;
                    }

                }

                LocalTrafficGroupRow result = new LocalTrafficGroupRow(_key, _name, _sub, upload, download, leaves, kind, groupTag);

                return result;
            }

            public LocalTrafficGroupRow ToBackgroundRow()
            {
                LocalTrafficGroupRow inner = ToRow(GroupKind.Background);
                string label = $"{inner.Children.Count} device{(inner.Children.Count == 1 ? string.Empty : "s")} — discovery only";
                inner.DisplayName = label;

                return inner;
            }
        }

        private sealed class LeafAccumulator
        {
            private readonly string _key;
            private readonly string _name;
            private readonly string? _sub;
            private long _upload;
            private long _download;
            private string? _tag;

            public LeafAccumulator(string key, string name, string? sub)
            {
                _key = key;
                _name = name;
                _sub = sub;
            }

            public void Add(long upload, long download, string? serviceTag)
            {
                _upload += upload;
                _download += download;
                _tag ??= serviceTag;
            }

            public LocalTrafficLeafRow ToRow()
            {
                LocalTrafficLeafRow result = new LocalTrafficLeafRow(_key, _name, _sub, _upload, _download, _tag);

                return result;
            }
        }
    }
}
