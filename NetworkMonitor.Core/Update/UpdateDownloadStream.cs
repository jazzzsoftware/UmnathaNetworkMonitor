using System;
using System.IO;
using System.Threading.Tasks;

namespace NetworkMonitor.Core.Update
{
    public sealed class UpdateDownloadStream : IAsyncDisposable
    {
        private readonly IDisposable? _owner;

        public UpdateDownloadStream(Stream content, long? contentLength, IDisposable? owner = null)
        {
            Content = content;
            ContentLength = contentLength;
            _owner = owner;
        }

        public Stream Content
        {
            get;
        }

        public long? ContentLength
        {
            get;
        }

        public async ValueTask DisposeAsync()
        {
            await Content.DisposeAsync();
            _owner?.Dispose();
        }
    }
}
