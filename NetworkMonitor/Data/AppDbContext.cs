using Microsoft.EntityFrameworkCore;
using NetworkMonitor.Models;

namespace NetworkMonitor.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<Device> Devices => Set<Device>();
        public DbSet<ScanSession> ScanSessions => Set<ScanSession>();
        public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();
        public DbSet<TrafficEntry> TrafficEntries => Set<TrafficEntry>();
        public DbSet<TrafficRollup> TrafficRollups => Set<TrafficRollup>();
        public DbSet<LocalTrafficRollup> LocalTrafficRollups => Set<LocalTrafficRollup>();
        public DbSet<DigestReport> DigestReports => Set<DigestReport>();
        public DbSet<SpeedTestResult> SpeedTestResults => Set<SpeedTestResult>();

        public static string DbPath =>
            Path.Combine(
                AppPaths.AppDataFolder,
                "networkmonitor.db");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Device>()
                .HasIndex(device => device.MacAddress)
                .IsUnique();

            modelBuilder.Entity<DeviceEvent>()
                .HasOne(deviceEvent => deviceEvent.Device)
                .WithMany()
                .HasForeignKey(deviceEvent => deviceEvent.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TrafficEntry>()
                .HasIndex(entry => new { entry.Timestamp, entry.ProcessName });

            modelBuilder.Entity<TrafficRollup>()
                .HasIndex(rollup => new { rollup.MinuteEpoch, rollup.ProcessName })
                .IsUnique();

            modelBuilder.Entity<LocalTrafficRollup>()
                .HasIndex(rollup => new { rollup.MinuteEpoch, rollup.RemoteIp })
                .IsUnique();

            modelBuilder.Entity<SpeedTestResult>()
                .HasIndex(result => result.Timestamp);

            modelBuilder.Entity<DeviceEvent>()
                .HasIndex(deviceEvent => deviceEvent.Timestamp);

            modelBuilder.Entity<Device>()
                .HasIndex(device => device.IsOnline);

            modelBuilder.Entity<Device>()
                .HasIndex(device => device.IsApproved);

            modelBuilder.Entity<DigestReport>()
                .HasIndex(report => report.PeriodEnd);

            modelBuilder.Entity<DigestReport>()
                .HasIndex(report => report.IsScheduled);
        }
    }
}
