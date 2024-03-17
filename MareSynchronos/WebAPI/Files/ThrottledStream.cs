namespace MareSynchronos.WebAPI.Files
{
    /// <summary>
    ///     Class for streaming data with throttling support.
    ///     Borrowed from https://github.com/bezzad/Downloader
    /// </summary>
    internal class ThrottledStream : Stream
    {
        public static long Infinite => long.MaxValue;
        private readonly Stream _baseStream;
        private long _bandwidthLimit;
        private Bandwidth _bandwidth;
        private CancellationTokenSource _bandwidthChangeTokenSource = new CancellationTokenSource();

        /// <summary>
        ///     Initializes a new instance of the <see cref="T:ThrottledStream" /> class.
        /// </summary>
        /// <param name="baseStream">The base stream.</param>
        /// <param name="bandwidthLimit">The maximum bytes per second that can be transferred through the base stream.</param>
        /// <exception cref="ArgumentNullException">Thrown when <see cref="baseStream" /> is a null reference.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="BandwidthLimit" /> is a negative value.</exception>
        public ThrottledStream(Stream baseStream, long bandwidthLimit)
        {
            if (bandwidthLimit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bandwidthLimit),
                    bandwidthLimit, "The maximum number of bytes per second can't be negative.");
            }

            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            BandwidthLimit = bandwidthLimit;
        }

        /// <summary>
        ///     Bandwidth Limit (in B/s)
        /// </summary>
        /// <value>The maximum bytes per second.</value>
        public long BandwidthLimit
        {
            get => _bandwidthLimit;
            set
            {
                if (_bandwidthLimit == value) return;
                _bandwidthLimit = value <= 0 ? Infinite : value;
                _bandwidth ??= new Bandwidth();
                _bandwidth.BandwidthLimit = _bandwidthLimit;
                _bandwidthChangeTokenSource.Cancel();
                _bandwidthChangeTokenSource.Dispose();
                _bandwidthChangeTokenSource = new();
            }
        }

        /// <inheritdoc />
        public override bool CanRead => _baseStream.CanRead;

        /// <inheritdoc />
        public override bool CanSeek => _baseStream.CanSeek;

        /// <inheritdoc />
        public override bool CanWrite => _baseStream.CanWrite;

        /// <inheritdoc />
        public override long Length => _baseStream.Length;

        /// <inheritdoc />
        public override long Position
        {
            get => _baseStream.Position;
            set => _baseStream.Position = value;
        }

        /// <inheritdoc />
        public override void Flush()
        {
            _baseStream.Flush();
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            return _baseStream.Seek(offset, origin);
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            _baseStream.SetLength(value);
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            Throttle(count).Wait();
            return _baseStream.Read(buffer, offset, count);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            await Throttle(count, cancellationToken).ConfigureAwait(false);
            return await _baseStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            Throttle(count).Wait();
            _baseStream.Write(buffer, offset, count);
        }

        /// <inheritdoc />
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Throttle(count, cancellationToken).ConfigureAwait(false);
            await _baseStream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        public override void Close()
        {
            _baseStream.Close();
            base.Close();
        }

        private async Task Throttle(int transmissionVolume, CancellationToken token = default)
        {
            // Make sure the buffer isn't empty.
            if (BandwidthLimit > 0 && transmissionVolume > 0)
            {
                // Calculate the time to sleep.
                _bandwidth.CalculateSpeed(transmissionVolume);
                await Sleep(_bandwidth.PopSpeedRetrieveTime(), token).ConfigureAwait(false);
            }
        }

        private async Task Sleep(int time, CancellationToken token = default)
        {
            try
            {
                if (time > 0)
                {
                    var bandWidthtoken = _bandwidthChangeTokenSource.Token;
                    var linked = CancellationTokenSource.CreateLinkedTokenSource(token, bandWidthtoken).Token;
                    await Task.Delay(time, linked).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return _baseStream?.ToString() ?? string.Empty;
        }

        private sealed class Bandwidth
        {
            private long _count;
            private int _lastSecondCheckpoint;
            private long _lastTransferredBytesCount;
            private int _speedRetrieveTime;
            public double Speed { get; private set; }
            public double AverageSpeed { get; private set; }
            public long BandwidthLimit { get; set; }

            public Bandwidth()
            {
                BandwidthLimit = long.MaxValue;
                Reset();
            }

            public void CalculateSpeed(long receivedBytesCount)
            {
                int elapsedTime = Environment.TickCount - _lastSecondCheckpoint + 1;
                receivedBytesCount = Interlocked.Add(ref _lastTransferredBytesCount, receivedBytesCount);
                double momentSpeed = receivedBytesCount * 1000 / (double)elapsedTime; // B/s

                if (1000 < elapsedTime)
                {
                    Speed = momentSpeed;
                    AverageSpeed = ((AverageSpeed * _count) + Speed) / (_count + 1);
                    _count++;
                    SecondCheckpoint();
                }

                if (momentSpeed >= BandwidthLimit)
                {
                    var expectedTime = receivedBytesCount * 1000 / BandwidthLimit;
                    Interlocked.Add(ref _speedRetrieveTime, (int)expectedTime - elapsedTime);
                }
            }

            public int PopSpeedRetrieveTime()
            {
                return Interlocked.Exchange(ref _speedRetrieveTime, 0);
            }

            public void Reset()
            {
                SecondCheckpoint();
                _count = 0;
                Speed = 0;
                AverageSpeed = 0;
            }

            private void SecondCheckpoint()
            {
                Interlocked.Exchange(ref _lastSecondCheckpoint, Environment.TickCount);
                Interlocked.Exchange(ref _lastTransferredBytesCount, 0);
            }
        }
    }
}
