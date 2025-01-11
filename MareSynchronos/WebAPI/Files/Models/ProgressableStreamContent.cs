using System.Net;

namespace MareSynchronos.WebAPI.Files.Models;

public class ProgressableStreamContent : StreamContent
{
    private const int _defaultBufferSize = 4096;
    private readonly int _bufferSize;
    private readonly IProgress<UploadProgress>? _progress;
    private readonly Stream _streamToWrite;
    private bool _contentConsumed;

    public ProgressableStreamContent(Stream streamToWrite, IProgress<UploadProgress>? downloader)
        : this(streamToWrite, _defaultBufferSize, downloader)
    {
    }

    public ProgressableStreamContent(Stream streamToWrite, int bufferSize, IProgress<UploadProgress>? progress)
        : base(streamToWrite, bufferSize)
    {
        if (streamToWrite == null)
        {
            throw new ArgumentNullException(nameof(streamToWrite));
        }

        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize));
        }

        _streamToWrite = streamToWrite;
        _bufferSize = bufferSize;
        _progress = progress;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _streamToWrite.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        PrepareContent();

        var buffer = new byte[_bufferSize];
        var size = _streamToWrite.Length;
        var uploaded = 0;

        using (_streamToWrite)
        {
            while (true)
            {
                var length = await _streamToWrite.ReadAsync(buffer).ConfigureAwait(false);
                if (length <= 0)
                {
                    break;
                }

                uploaded += length;
                _progress?.Report(new UploadProgress(uploaded, size));
                await stream.WriteAsync(buffer.AsMemory(0, length)).ConfigureAwait(false);
            }
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _streamToWrite.Length;
        return true;
    }

    private void PrepareContent()
    {
        if (_contentConsumed)
        {
            if (_streamToWrite.CanSeek)
            {
                _streamToWrite.Position = 0;
            }
            else
            {
                throw new InvalidOperationException("The stream has already been read.");
            }
        }

        _contentConsumed = true;
    }
}