using System;
using NetworkMonitor.Models.Devices;
using Xunit;

namespace NetworkMonitor.Tests
{
    public class DeviceTests
    {
        [Fact]
        public void DisplayNamePrefersFriendlyName()
        {
            Device device = new()
            {
                FriendlyName = "Mark's Laptop",
                Hostname = "laptop.local",
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("Mark's Laptop", displayName);
        }

        [Fact]
        public void DisplayNameFallsBackToHostnameWhenNoFriendlyName()
        {
            Device device = new()
            {
                FriendlyName = null,
                Hostname = "laptop.local",
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("laptop.local", displayName);
        }

        [Fact]
        public void DisplayNameFallsBackToIpWhenNoFriendlyNameOrHostname()
        {
            Device device = new()
            {
                FriendlyName = null,
                Hostname = null,
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("192.168.1.50", displayName);
        }

        [Fact]
        public void DisplayNamePrefersFriendlyNameOverMdnsName()
        {
            Device device = new()
            {
                FriendlyName = "Mark's Laptop",
                MdnsName = "Kitchen HomePod",
                Hostname = "laptop.local",
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("Mark's Laptop", displayName);
        }

        [Fact]
        public void DisplayNameUsesMdnsNameWhenNoFriendlyName()
        {
            Device device = new()
            {
                FriendlyName = null,
                MdnsName = "Kitchen HomePod",
                Hostname = "laptop.local",
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("Kitchen HomePod", displayName);
        }

        [Fact]
        public void DisplayNameFallsBackToHostnameWhenNoFriendlyOrMdnsName()
        {
            Device device = new()
            {
                FriendlyName = null,
                MdnsName = null,
                Hostname = "laptop.local",
                IpAddress = "192.168.1.50"
            };

            string displayName = device.DisplayName;

            Assert.Equal("laptop.local", displayName);
        }

        [Fact]
        public void LastSeenLabelIsOnlineWhenOnline()
        {
            Device device = new()
            {
                IsOnline = true,
                LastSeen = DateTime.UtcNow.AddDays(-3)
            };

            string label = device.LastSeenLabel;

            Assert.Equal("Online", label);
        }

        [Fact]
        public void LastSeenLabelUsesMinutesWithinTheHour()
        {
            Device device = new()
            {
                IsOnline = false,
                LastSeen = DateTime.UtcNow.AddMinutes(-5)
            };

            string label = device.LastSeenLabel;

            Assert.EndsWith("m ago", label);
        }

        [Fact]
        public void LastSeenLabelUsesHoursWithinTheDay()
        {
            Device device = new()
            {
                IsOnline = false,
                LastSeen = DateTime.UtcNow.AddHours(-2)
            };

            string label = device.LastSeenLabel;

            Assert.Equal("2h ago", label);
        }

        [Fact]
        public void LastSeenLabelUsesDaysBeyondADay()
        {
            Device device = new()
            {
                IsOnline = false,
                LastSeen = DateTime.UtcNow.AddDays(-3)
            };

            string label = device.LastSeenLabel;

            Assert.Equal("3d ago", label);
        }

        [Theory]
        [InlineData(DeviceType.Router, "🌐")]
        [InlineData(DeviceType.PC, "💻")]
        [InlineData(DeviceType.Mobile, "📱")]
        [InlineData(DeviceType.Unknown, "❓")]
        public void TypeIconMatchesDeviceType(DeviceType type, string expectedIcon)
        {
            Device device = new()
            {
                Type = type
            };

            string icon = device.TypeIcon;

            Assert.Equal(expectedIcon, icon);
        }
    }
}
