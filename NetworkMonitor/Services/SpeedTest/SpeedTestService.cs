using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetworkMonitor.Models;
using NetworkMonitor.Services.Platform;

namespace NetworkMonitor.Services.SpeedTest
{
    public class SpeedTestService(HttpClient httpClient)
    {
        private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=";
        private const string UploadUrl = "https://speed.cloudflare.com/__up";
        private const long DownloadBytes = 50_000_000;
        private const long UploadBytes = 50_000_000;
        private const int LatencySamples = 10;

        public async Task<SpeedTestResult> RunAsync(CancellationToken ct = default)
        {
            SpeedTestResult result;

            try
            {
                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
                CancellationToken measurementToken = timeoutCts.Token;

                List<double> latencies = new();
                string server = string.Empty;

                using (HttpResponseMessage warmup = await httpClient.GetAsync(
                    DownloadUrl + "0", HttpCompletionOption.ResponseHeadersRead, measurementToken))
                {
                    warmup.EnsureSuccessStatusCode();
                    await warmup.Content.ReadAsByteArrayAsync(measurementToken);

                    if (warmup.Headers.TryGetValues("cf-ray", out IEnumerable<string>? rayValues))
                    {

                        foreach (string ray in rayValues)
                        {
                            server = SpeedTestMath.ColoFromCfRay(ray);

                            break;
                        }

                    }

                }

                for (int index = 0; index < LatencySamples; index++)
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    using HttpResponseMessage response = await httpClient.GetAsync(
                        DownloadUrl + "0", HttpCompletionOption.ResponseHeadersRead, measurementToken);

                    response.EnsureSuccessStatusCode();
                    await response.Content.ReadAsByteArrayAsync(measurementToken);
                    stopwatch.Stop();

                    double networkMs = stopwatch.Elapsed.TotalMilliseconds - ServerProcessingMs(response);

                    if (networkMs < 0.0)
                    {
                        networkMs = 0.0;
                    }

                    latencies.Add(networkMs);
                }

                double downloadMbps = await MeasureDownloadAsync(measurementToken);
                double uploadMbps = await MeasureUploadAsync(measurementToken);

                result = new SpeedTestResult
                {
                    Timestamp = DateTime.UtcNow,
                    DownloadMbps = downloadMbps,
                    UploadMbps = uploadMbps,
                    LatencyMs = SpeedTestMath.Min(latencies),
                    JitterMs = SpeedTestMath.Jitter(latencies),
                    Server = server,
                    Success = true
                };
            }
            catch (Exception exception)
            {
                AppLog.Error("SpeedTestService.Run", exception);

                result = new SpeedTestResult
                {
                    Timestamp = DateTime.UtcNow,
                    Success = false,
                    Error = exception.Message
                };
            }

            return result;
        }

        private static double ServerProcessingMs(HttpResponseMessage response)
        {
            double duration = 0.0;

            if (response.Headers.TryGetValues("Server-Timing", out IEnumerable<string>? serverTimingValues))
            {

                foreach (string headerValue in serverTimingValues)
                {

                    foreach (string metric in headerValue.Split(','))
                    {
                        string trimmed = metric.Trim();

                        if (trimmed.StartsWith("cfRequestDuration", StringComparison.OrdinalIgnoreCase))
                        {
                            int durIndex = trimmed.IndexOf("dur=", StringComparison.OrdinalIgnoreCase);

                            if (durIndex >= 0)
                            {
                                string durText = trimmed.Substring(durIndex + 4).Trim().Trim('"');

                                if (double.TryParse(durText, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                                {
                                    duration = parsed;
                                }

                            }

                        }

                    }

                }

            }

            return duration;
        }

        private async Task<double> MeasureDownloadAsync(CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            long total = 0;

            using HttpResponseMessage response = await httpClient.GetAsync(
                DownloadUrl + DownloadBytes, HttpCompletionOption.ResponseHeadersRead, ct);

            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            byte[] buffer = new byte[81920];
            int read = await stream.ReadAsync(buffer, ct);

            while (read > 0)
            {
                total += read;
                read = await stream.ReadAsync(buffer, ct);
            }

            stopwatch.Stop();
            double mbps = SpeedTestMath.ToMbps(total, stopwatch.Elapsed);

            return mbps;
        }

        private async Task<double> MeasureUploadAsync(CancellationToken ct)
        {
            byte[] payload = new byte[UploadBytes];
            Stopwatch stopwatch = Stopwatch.StartNew();

            using ByteArrayContent content = new ByteArrayContent(payload);
            using HttpResponseMessage response = await httpClient.PostAsync(UploadUrl, content, ct);

            response.EnsureSuccessStatusCode();
            stopwatch.Stop();
            double mbps = SpeedTestMath.ToMbps(payload.LongLength, stopwatch.Elapsed);

            return mbps;
        }
    }
}
