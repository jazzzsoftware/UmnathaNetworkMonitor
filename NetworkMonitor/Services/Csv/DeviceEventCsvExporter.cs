using System.Collections.Generic;
using System.Text;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Core.Csv;

namespace NetworkMonitor.Services.Csv
{
    public static class DeviceEventCsvExporter
    {
        public static string ToCsv(IEnumerable<DeviceEvent> events)
        {
            StringBuilder builder = new();
            builder.AppendLine("Time,Event,Name,IP Address,MAC Address,Vendor");

            foreach (DeviceEvent deviceEvent in events)
            {
                string line = string.Join(",",
                    CsvField.Escape(deviceEvent.TimestampLabel),
                    CsvField.Escape(deviceEvent.EventLabel),
                    CsvField.Escape(deviceEvent.Device?.DisplayName ?? string.Empty),
                    CsvField.Escape(deviceEvent.Device?.IpAddress ?? string.Empty),
                    CsvField.Escape(deviceEvent.Device?.MacAddress ?? string.Empty),
                    CsvField.Escape(deviceEvent.Device?.Vendor ?? string.Empty));

                builder.AppendLine(line);
            }

            string result = builder.ToString();

            return result;
        }
    }
}
