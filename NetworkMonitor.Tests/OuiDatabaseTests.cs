using System;
using System.IO;
using Xunit;
using NetworkMonitor.Core.Data;

namespace NetworkMonitor.Tests
{
    public class OuiDatabaseTests : IDisposable
    {
        private readonly string _ouiFilePath;

        public OuiDatabaseTests()
        {
            _ouiFilePath = Path.Combine(Path.GetTempPath(), $"oui-test-{Guid.NewGuid():N}.txt");
            string contents =
                "00-1B-44   (hex)\t\tSanDisk Corporation\r\n" +
                "AC-DE-48   (hex)\t\tPrivate\r\n" +
                "garbage line that should be ignored\r\n";
            File.WriteAllText(_ouiFilePath, contents);
        }

        public void Dispose()
        {

            if (File.Exists(_ouiFilePath))
            {
                File.Delete(_ouiFilePath);
            }

        }

        [Fact]
        public void LookupReturnsVendorForKnownPrefix()
        {
            OuiDatabase database = new();
            database.Load(_ouiFilePath);

            string? vendor = database.Lookup("00:1B:44:11:22:33");

            Assert.Equal("SanDisk Corporation", vendor);
        }

        [Fact]
        public void LookupIsCaseInsensitive()
        {
            OuiDatabase database = new();
            database.Load(_ouiFilePath);

            string? vendor = database.Lookup("ac:de:48:aa:bb:cc");

            Assert.Equal("Private", vendor);
        }

        [Fact]
        public void LookupReturnsNullForUnknownPrefix()
        {
            OuiDatabase database = new();
            database.Load(_ouiFilePath);

            string? vendor = database.Lookup("FF:FF:FF:00:00:00");

            Assert.Null(vendor);
        }

        [Fact]
        public void LookupReturnsNullForShortMacAddress()
        {
            OuiDatabase database = new();
            database.Load(_ouiFilePath);

            string? vendor = database.Lookup("00:1B");

            Assert.Null(vendor);
        }

        [Fact]
        public void LoadMissingFileLeavesDatabaseEmpty()
        {
            OuiDatabase database = new();
            database.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt"));

            string? vendor = database.Lookup("00:1B:44:11:22:33");

            Assert.Null(vendor);
        }
    }
}
