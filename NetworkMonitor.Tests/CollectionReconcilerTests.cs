using System.Collections.ObjectModel;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Services.Common;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class CollectionReconcilerTests
    {
        private static Device MakeDevice(int id, string ip, bool isOnline = true)
        {
            Device device = new Device
            {
                Id = id,
                IpAddress = ip,
                IsOnline = isOnline
            };

            return device;
        }

        [Fact]
        public void SyncOrderedKeepsExistingInstancesForMatchedKeys()
        {
            Device first = MakeDevice(1, "10.0.0.1");
            Device second = MakeDevice(2, "10.0.0.2");
            ObservableCollection<Device> collection = new() { first, second };

            List<Device> target = new() { MakeDevice(1, "10.0.0.1"), MakeDevice(2, "10.0.0.2") };
            CollectionReconciler.SyncOrdered(collection, target, device => device.Id, static (existing, incoming) => existing.CopyValuesFrom(incoming));

            Assert.Same(first, collection[0]);
            Assert.Same(second, collection[1]);
        }

        [Fact]
        public void SyncOrderedAppliesValuesToMatchedInstance()
        {
            Device existing = MakeDevice(1, "10.0.0.1", isOnline: true);
            ObservableCollection<Device> collection = new() { existing };

            List<Device> target = new() { MakeDevice(1, "10.0.0.9", isOnline: false) };
            CollectionReconciler.SyncOrdered(collection, target, device => device.Id, static (matched, incoming) => matched.CopyValuesFrom(incoming));

            Assert.Same(existing, collection[0]);
            Assert.Equal("10.0.0.9", existing.IpAddress);
            Assert.False(existing.IsOnline);
        }

        [Fact]
        public void SyncOrderedAddsNewAndRemovesGone()
        {
            Device kept = MakeDevice(1, "10.0.0.1");
            Device gone = MakeDevice(2, "10.0.0.2");
            ObservableCollection<Device> collection = new() { kept, gone };

            Device added = MakeDevice(3, "10.0.0.3");
            List<Device> target = new() { kept, added };
            CollectionReconciler.SyncOrdered(collection, target, device => device.Id, static (existing, incoming) => existing.CopyValuesFrom(incoming));

            Assert.Equal(2, collection.Count);
            Assert.Same(kept, collection[0]);
            Assert.Same(added, collection[1]);
            Assert.DoesNotContain(gone, collection);
        }

        [Fact]
        public void SyncOrderedReordersToMatchTarget()
        {
            Device a = MakeDevice(1, "10.0.0.1");
            Device b = MakeDevice(2, "10.0.0.2");
            Device c = MakeDevice(3, "10.0.0.3");
            ObservableCollection<Device> collection = new() { a, b, c };

            List<Device> target = new() { c, a, b };
            CollectionReconciler.SyncOrdered(collection, target, device => device.Id, static (existing, incoming) => existing.CopyValuesFrom(incoming));

            Assert.Equal(new[] { 3, 1, 2 }, collection.Select(device => device.Id).ToArray());
            Assert.Same(c, collection[0]);
            Assert.Same(a, collection[1]);
            Assert.Same(b, collection[2]);
        }

        [Fact]
        public void MergeUnorderedUpdatesMatchedInstanceInPlace()
        {
            Device existing = MakeDevice(1, "10.0.0.1", isOnline: true);
            List<Device> target = new() { existing };

            List<Device> fresh = new() { MakeDevice(1, "10.0.0.5", isOnline: false) };
            CollectionReconciler.MergeUnordered(target, fresh, device => device.Id, static (matched, incoming) => matched.CopyValuesFrom(incoming));

            Assert.Single(target);
            Assert.Same(existing, target[0]);
            Assert.Equal("10.0.0.5", existing.IpAddress);
            Assert.False(existing.IsOnline);
        }

        [Fact]
        public void MergeUnorderedAddsNewAndRemovesGone()
        {
            Device kept = MakeDevice(1, "10.0.0.1");
            Device gone = MakeDevice(2, "10.0.0.2");
            List<Device> target = new() { kept, gone };

            Device added = MakeDevice(3, "10.0.0.3");
            List<Device> fresh = new() { MakeDevice(1, "10.0.0.1"), added };
            CollectionReconciler.MergeUnordered(target, fresh, device => device.Id, static (existing, incoming) => existing.CopyValuesFrom(incoming));

            Assert.Equal(2, target.Count);
            Assert.Same(kept, target.Single(device => device.Id == 1));
            Assert.Contains(added, target);
            Assert.DoesNotContain(gone, target);
        }
    }
}
