using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NetworkMonitor.Models.Devices;
using NetworkMonitor.Services.Scanning;

namespace NetworkMonitor.Services.Csv
{
    public static class DeviceCsvImporter
    {
        public static IReadOnlyList<Device> Parse(string csvText)
        {
            List<Device> devices = [];
            List<List<string>> rows = ParseRows(csvText);

            if (rows.Count > 1)
            {
                Dictionary<string, int> columns = MapColumns(rows[0]);

                if (IsDeviceHistoryFormat(columns))
                {
                    throw new InvalidOperationException("This is a device history export, not an approved devices list. Export the device list and import that instead.");
                }

                if (columns.ContainsKey("MAC Address"))
                {

                    for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
                    {
                        List<string> row = rows[rowIndex];
                        string mac = Get(row, columns, "MAC Address");

                        if (!string.IsNullOrWhiteSpace(mac))
                        {
                            Device device = BuildDevice(row, columns);
                            devices.Add(device);
                        }

                    }

                }

            }

            return devices;
        }

        private static List<List<string>> ParseRows(string text)
        {
            List<List<string>> rows = [];
            List<string> currentRow = [];
            StringBuilder field = new();
            bool inQuotes = false;
            int index = 0;

            while (index < text.Length)
            {
                char character = text[index];

                if (inQuotes)
                {

                    if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else if (character == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        field.Append(character);
                    }

                }
                else if (character == '"')
                {
                    inQuotes = true;
                }
                else if (character == ',')
                {
                    currentRow.Add(field.ToString());
                    field.Clear();
                }
                else if (character == '\n')
                {
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rows.Add(currentRow);
                    currentRow = [];
                }
                else if (character != '\r')
                {
                    field.Append(character);
                }

                index++;
            }

            if (field.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(field.ToString());
                rows.Add(currentRow);
            }

            return rows;
        }

        private static bool IsDeviceHistoryFormat(Dictionary<string, int> columns)
        {
            bool isHistory = columns.ContainsKey("Event") && columns.ContainsKey("Time");

            return isHistory;
        }

        private static Dictionary<string, int> MapColumns(List<string> header)
        {
            Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < header.Count; index++)
            {
                string name = header[index].Trim();

                if (!string.IsNullOrEmpty(name) && !columns.ContainsKey(name))
                {
                    columns[name] = index;
                }

            }

            return columns;
        }

        private static Device BuildDevice(List<string> row, Dictionary<string, int> columns)
        {
            string name = Get(row, columns, "Name");
            string typeText = Get(row, columns, "Type");
            string ip = Get(row, columns, "IP Address");
            string mac = Get(row, columns, "MAC Address");
            string vendor = Get(row, columns, "Vendor");
            string hostname = Get(row, columns, "Hostname");
            string notes = Get(row, columns, "Notes");
            DateTime firstSeen = ParseTimestamp(Get(row, columns, "First Seen"));
            DateTime lastSeen = ParseTimestamp(Get(row, columns, "Last Seen"));
            DeviceType type = Enum.TryParse(typeText.Trim(), true, out DeviceType parsedType) ? parsedType : DeviceType.Unknown;

            Device device = new()
            {
                MacAddress = MacNormalizer.Normalize(mac),
                IpAddress = ip.Trim(),
                Hostname = string.IsNullOrWhiteSpace(hostname) ? null : hostname.Trim(),
                Vendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor.Trim(),
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                Type = type,
                FriendlyName = ResolveFriendlyName(name, hostname, ip),
                IsApproved = true,
                IsOnline = false,
                FirstSeen = firstSeen,
                LastSeen = lastSeen
            };

            return device;
        }

        private static string? ResolveFriendlyName(string name, string hostname, string ip)
        {
            string? friendly = null;
            string trimmed = name.Trim();

            if (!string.IsNullOrWhiteSpace(trimmed)
                && !string.Equals(trimmed, hostname.Trim(), StringComparison.OrdinalIgnoreCase)
                && trimmed != ip.Trim())
            {
                friendly = trimmed;
            }

            return friendly;
        }

        private static DateTime ParseTimestamp(string text)
        {
            DateTime result = DateTime.UtcNow;

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
            {
                result = parsed.ToUniversalTime();
            }

            return result;
        }

        private static string Get(List<string> row, Dictionary<string, int> columns, string key)
        {
            string value = string.Empty;

            if (columns.TryGetValue(key, out int index) && index < row.Count)
            {
                value = row[index];
            }

            return value;
        }
    }
}
