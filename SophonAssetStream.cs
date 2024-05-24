using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable CommentTypo
namespace Hi3Helper.Sophon
{
    public class SophonAssetStream : Stream
    {
        #region Initializers

        private protected HttpRequestMessage  NetworkRequest;
        private protected HttpResponseMessage NetworkResponse;
    #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private protected Stream NetworkStream;
    #pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public HttpStatusCode StatusCode;
        public bool           IsSuccessStatusCode;

        #endregion

        public static async Task<Stream> CreateStreamAsync(HttpClient client,    string url, long? startOffset,
                                                           long?      endOffset, CancellationToken token)
        {
            if (startOffset == null)
            {
                startOffset = 0;
            }

            SophonAssetStream httpResponseInputStream = new SophonAssetStream();
            httpResponseInputStream.NetworkRequest = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method     = HttpMethod.Get
            };

            token.ThrowIfCancellationRequested();

            httpResponseInputStream.NetworkRequest.Headers.Range = new RangeHeaderValue(startOffset, endOffset);
            httpResponseInputStream.NetworkResponse = await client
               .SendAsync(httpResponseInputStream.NetworkRequest, HttpCompletionOption.ResponseHeadersRead, token);

            httpResponseInputStream.StatusCode          = httpResponseInputStream.NetworkResponse.StatusCode;
            httpResponseInputStream.IsSuccessStatusCode = httpResponseInputStream.NetworkResponse.IsSuccessStatusCode;
            if (httpResponseInputStream.IsSuccessStatusCode)
            {
            #if NET6_0_OR_GREATER
                httpResponseInputStream.NetworkStream = await httpResponseInputStream.NetworkResponse
                   .Content.ReadAsStreamAsync(token);
            #else
                httpResponseInputStream.NetworkStream = await httpResponseInputStream.NetworkResponse
                   .Content.ReadAsStreamAsync();
            #endif
                return httpResponseInputStream;
            }

            if ((int)httpResponseInputStream.StatusCode != 416)
            {
                throw new
                    HttpRequestException(string.Format("HttpResponse for URL: \"{1}\" has returned unsuccessful code: {0}",
                                                       httpResponseInputStream.NetworkResponse.StatusCode, url));
            }
        #if NET6_0_OR_GREATER
            await httpResponseInputStream.DisposeAsync();
        #else
            httpResponseInputStream.Dispose();
        #endif
            return null;
        }

        ~SophonAssetStream()
        {
            Dispose();
        }

    #if NET6_0_OR_GREATER
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken =
 default)
            => await NetworkStream.ReadAsync(buffer, cancellationToken);

        public override int Read(Span<byte> buffer)
            => NetworkStream.Read(buffer);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
    #endif

        public override async Task<int> ReadAsync(byte[]            buffer, int offset, int count,
                                                  CancellationToken cancellationToken = default)
        {
            return await NetworkStream.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return NetworkStream.Read(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override void Flush()
        {
            NetworkStream.Flush();
        }

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                NetworkRequest?.Dispose();
                NetworkResponse?.Dispose();
                NetworkStream?.Dispose();
            }

            GC.SuppressFinalize(this);
        }

    #if NET6_0_OR_GREATER
        public override async ValueTask DisposeAsync()
        {
            NetworkRequest?.Dispose();
            NetworkResponse?.Dispose();
            if (NetworkStream != null) await NetworkStream.DisposeAsync();

            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    #endif
    }
}