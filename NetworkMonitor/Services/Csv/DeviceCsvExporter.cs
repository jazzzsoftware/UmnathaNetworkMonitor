using System.Collections.Generic;
using System.Text;
using NetworkMonitor.Models;

namespace NetworkMonitor.Services.Csv
{
    public static class DeviceCsvExporter
    {
        public static string ToCsv(IEnumerable<Device> devices)
        {
            StringBuilder builder = new();
            builder.AppendLine("Name,Type,IP Address,MAC Address,Vendor,Hostname,Online,First Seen,Last Seen,Notes");

            foreach (Device device in devices)
            {
                string onlineLabel = device.IsOnline ? "Yes" : "No";
                string firstSeen = device.FirstSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                string lastSeen = device.LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

                string line = string.Join(",",
                    CsvField.Escape(device.DisplayName),
                    CsvField.Escape(device.Type.ToString()),
                    CsvField.Escape(device.IpAddress),
                    CsvField.Escape(device.MacAddress),
                    CsvField.Escape(device.Vendor ?? string.Empty),
                    CsvField.Escape(device.Hostname ?? string.Empty),
                    CsvField.Escape(onlineLabel),
                    CsvField.Escape(firstSeen),
                    CsvField.Escape(lastSeen),
                    CsvField.Escape(device.Notes ?? string.Empty));

                builder.AppendLine(line);
            }

            string result = builder.ToString();

            return result;
        }
    }
}
