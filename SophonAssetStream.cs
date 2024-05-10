using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Hi3Helper.Sophon
{
    public class SophonAssetStream : Stream
    {
        private protected HttpRequestMessage? _networkRequest;
        private protected HttpResponseMessage? _networkResponse;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private protected Stream _networkStream;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private protected long _networkLength;
        private protected long _currentPosition = 0;
        public HttpStatusCode _statusCode;
        public bool _isSuccessStatusCode;

        public static async Task<SophonAssetStream?> CreateStreamAsync(HttpClient client, string url, long? startOffset, long? endOffset, CancellationToken token)
        {
            startOffset ??= 0;
            SophonAssetStream httpResponseInputStream = new SophonAssetStream();
            httpResponseInputStream._networkRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Get
            };

            token.ThrowIfCancellationRequested();

            httpResponseInputStream._networkRequest.Headers.Range = new RangeHeaderValue(startOffset, endOffset);
            httpResponseInputStream._networkResponse = await client
                .SendAsync(httpResponseInputStream._networkRequest, HttpCompletionOption.ResponseHeadersRead, token);

            httpResponseInputStream._statusCode = httpResponseInputStream._networkResponse.StatusCode;
            httpResponseInputStream._isSuccessStatusCode = httpResponseInputStream._networkResponse.IsSuccessStatusCode;
            if (httpResponseInputStream._isSuccessStatusCode)
            {
                httpResponseInputStream._networkLength = httpResponseInputStream._networkResponse
                    .Content.Headers.ContentLength ?? 0;
                httpResponseInputStream._networkStream = await httpResponseInputStream._networkResponse
                    .Content.ReadAsStreamAsync(token);
                return httpResponseInputStream;
            }

            if ((int)httpResponseInputStream._statusCode == 416)
            {
                await httpResponseInputStream.DisposeAsync();
                return null;
            }

            throw new HttpRequestException(string.Format("HttpResponse for URL: \"{1}\" has returned unsuccessful code: {0}", httpResponseInputStream._networkResponse.StatusCode, url));
        }

        ~SophonAssetStream() => Dispose();

        public int ReadUntilFull(Span<byte> buffer)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = _networkStream.Read(buffer.Slice(totalRead));
                if (read == 0) return totalRead;

                totalRead += read;
                _currentPosition += read;
            }
            return totalRead;
        }

        public async ValueTask<int> ReadUntilFullAsync(Memory<byte> buffer, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = await _networkStream.ReadAsync(buffer.Slice(totalRead), token);
                if (read == 0) return totalRead;

                totalRead += read;
                _currentPosition += read;
            }
            return totalRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => await ReadUntilFullAsync(buffer, cancellationToken);
        public override int Read(Span<byte> buffer) => ReadUntilFull(buffer);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

        private async ValueTask<int> ReadUntilFullAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (offset < count)
            {
                int read = await _networkStream
                    .ReadAsync(buffer.AsMemory(offset), token);
                if (read == 0) return totalRead;

                totalRead += read;
                offset += read;
                _currentPosition += read;
            }
            return totalRead;
        }

        private int ReadUntilFull(byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (offset < count)
            {
                int read = _networkStream.Read(buffer.AsSpan(offset));
                if (read == 0) return totalRead;

                totalRead += read;
                offset += read;
                _currentPosition += read;
            }
            return totalRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) => await ReadUntilFullAsync(buffer, offset, count, cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => ReadUntilFull(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            _networkStream.Flush();
        }

        public override long Length
        {
            get { return _networkLength; }
        }

        public override long Position
        {
            get { return _currentPosition; }
            set { throw new NotSupportedException(); }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _networkRequest?.Dispose();
                _networkResponse?.Dispose();
                _networkStream?.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        public override async ValueTask DisposeAsync()
        {
            _networkRequest?.Dispose();
            _networkResponse?.Dispose();
            if (_networkStream != null)
                await _networkStream.DisposeAsync();

            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
