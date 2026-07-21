using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
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
        private const int LatencySamples = 10;
        private const int ParallelStreams = 6;
        private const int WarmupSeconds = 2;
        private const int MeasureSeconds = 6;
        private const long StreamRequestBytes = 99_999_999;
        private const int UploadBufferBytes = 262_144;

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
            double mbps = await MeasureAsync(upload: false, ct);

            return mbps;
        }

        private async Task<double> MeasureUploadAsync(CancellationToken ct)
        {
            double mbps = await MeasureAsync(upload: true, ct);

            return mbps;
        }

        private async Task<double> MeasureAsync(bool upload, CancellationToken ct)
        {
            long[] counter = new long[1];

            using CancellationTokenSource streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            List<Task> streams = new List<Task>();

            for (int index = 0; index < ParallelStreams; index++)
            {

                if (upload)
                {
                    streams.Add(UploadStreamAsync(counter, streamCts.Token));
                }
                else
                {
                    streams.Add(DownloadStreamAsync(counter, streamCts.Token));
                }

            }

            await Task.Delay(TimeSpan.FromSeconds(WarmupSeconds), ct);

            long startBytes = Interlocked.Read(ref counter[0]);
            long startTicks = Stopwatch.GetTimestamp();

            await Task.Delay(TimeSpan.FromSeconds(MeasureSeconds), ct);

            long endBytes = Interlocked.Read(ref counter[0]);
            long endTicks = Stopwatch.GetTimestamp();

            streamCts.Cancel();

            try
            {
                await Task.WhenAll(streams);
            }
            catch (Exception)
            {
            }

            long deltaBytes = endBytes - startBytes;
            TimeSpan elapsed = TimeSpan.FromSeconds((endTicks - startTicks) / (double)Stopwatch.Frequency);
            double mbps = SpeedTestMath.ToMbps(deltaBytes, elapsed);

            return mbps;
        }

        private async Task DownloadStreamAsync(long[] counter, CancellationToken token)
        {

            try
            {

                while (!token.IsCancellationRequested)
                {
                    using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl + StreamRequestBytes)
                    {
                        Version = HttpVersion.Version11,
                        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                    };

                    using HttpResponseMessage response = await httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, token);

                    response.EnsureSuccessStatusCode();

                    await using Stream stream = await response.Content.ReadAsStreamAsync(token);
                    byte[] buffer = new byte[81920];
                    int read = await stream.ReadAsync(buffer, token);

                    while (read > 0)
                    {
                        Interlocked.Add(ref counter[0], read);
                        read = await stream.ReadAsync(buffer, token);
                    }

                }

            }
            catch (Exception)
            {
            }

        }

        private async Task UploadStreamAsync(long[] counter, CancellationToken token)
        {

            try
            {
                using CountingUploadContent content = new CountingUploadContent(counter, token);
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, UploadUrl)
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                    Content = content
                };

                using HttpResponseMessage response = await httpClient.SendAsync(request, token);

                response.EnsureSuccessStatusCode();
            }
            catch (Exception)
            {
            }

        }

        private sealed class CountingUploadContent : HttpContent
        {
            private static readonly byte[] Payload = new byte[UploadBufferBytes];

            private readonly long[] _counter;
            private readonly CancellationToken _token;

            public CountingUploadContent(long[] counter, CancellationToken token)
            {
                _counter = counter;
                _token = token;
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {

                try
                {

                    while (!_token.IsCancellationRequested)
                    {
                        await stream.WriteAsync(Payload, _token);
                        Interlocked.Add(ref _counter[0], Payload.Length);
                    }

                }
                catch (Exception)
                {
                }

            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;

                bool computed = false;

                return computed;
            }
        }
    }
}
