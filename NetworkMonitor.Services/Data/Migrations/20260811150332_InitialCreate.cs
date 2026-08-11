using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NetworkMonitor.Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MacAddress = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", nullable: true),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: true),
                    MdnsName = table.Column<string>(type: "TEXT", nullable: true),
                    Vendor = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsHost = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOnline = table.Column<bool>(type: "INTEGER", nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DigestReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Headline = table.Column<string>(type: "TEXT", nullable: false),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsScheduled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigestReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalTrafficEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessPath = table.Column<string>(type: "TEXT", nullable: true),
                    RemoteIp = table.Column<string>(type: "TEXT", nullable: false),
                    Protocol = table.Column<int>(type: "INTEGER", nullable: false),
                    RemotePort = table.Column<int>(type: "INTEGER", nullable: false),
                    BytesUploaded = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesDownloaded = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalTrafficEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalTrafficRollups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MinuteEpoch = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessPath = table.Column<string>(type: "TEXT", nullable: true),
                    RemoteIp = table.Column<string>(type: "TEXT", nullable: false),
                    Protocol = table.Column<int>(type: "INTEGER", nullable: false),
                    RemotePort = table.Column<int>(type: "INTEGER", nullable: false),
                    BytesUploaded = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesDownloaded = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalTrafficRollups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DevicesFound = table.Column<int>(type: "INTEGER", nullable: false),
                    NewDevices = table.Column<int>(type: "INTEGER", nullable: false),
                    DevicesGone = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpeedTestResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DownloadMbps = table.Column<double>(type: "REAL", nullable: false),
                    UploadMbps = table.Column<double>(type: "REAL", nullable: false),
                    LatencyMs = table.Column<double>(type: "REAL", nullable: false),
                    JitterMs = table.Column<double>(type: "REAL", nullable: false),
                    Server = table.Column<string>(type: "TEXT", nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeedTestResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrafficEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", nullable: false),
                    BytesUploaded = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesDownloaded = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessPath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrafficEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrafficRollups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MinuteEpoch = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", nullable: false),
                    BytesUploaded = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesDownloaded = table.Column<long>(type: "INTEGER", nullable: false),
                    ProcessPath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrafficRollups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeviceId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceEvents_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvents_DeviceId",
                table: "DeviceEvents",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvents_Timestamp",
                table: "DeviceEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_IsApproved",
                table: "Devices",
                column: "IsApproved");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_IsOnline",
                table: "Devices",
                column: "IsOnline");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_MacAddress",
                table: "Devices",
                column: "MacAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigestReports_IsScheduled",
                table: "DigestReports",
                column: "IsScheduled");

            migrationBuilder.CreateIndex(
                name: "IX_DigestReports_PeriodEnd",
                table: "DigestReports",
                column: "PeriodEnd");

            migrationBuilder.CreateIndex(
                name: "IX_LocalTrafficEntries_Timestamp",
                table: "LocalTrafficEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_LocalTrafficRollups_MinuteEpoch_ProcessName_RemoteIp_Protocol_RemotePort",
                table: "LocalTrafficRollups",
                columns: new[] { "MinuteEpoch", "ProcessName", "RemoteIp", "Protocol", "RemotePort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpeedTestResults_Timestamp",
                table: "SpeedTestResults",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_TrafficEntries_Timestamp_ProcessName",
                table: "TrafficEntries",
                columns: new[] { "Timestamp", "ProcessName" });

            migrationBuilder.CreateIndex(
                name: "IX_TrafficRollups_MinuteEpoch_ProcessName",
                table: "TrafficRollups",
                columns: new[] { "MinuteEpoch", "ProcessName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceEvents");

            migrationBuilder.DropTable(
                name: "DigestReports");

            migrationBuilder.DropTable(
                name: "LocalTrafficEntries");

            migrationBuilder.DropTable(
                name: "LocalTrafficRollups");

            migrationBuilder.DropTable(
                name: "ScanSessions");

            migrationBuilder.DropTable(
                name: "SpeedTestResults");

            migrationBuilder.DropTable(
                name: "TrafficEntries");

            migrationBuilder.DropTable(
                name: "TrafficRollups");

            migrationBuilder.DropTable(
                name: "Devices");
        }
    }
}
