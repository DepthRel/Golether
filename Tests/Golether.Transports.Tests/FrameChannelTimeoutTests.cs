namespace Golether.Transports.Tests;

/// <summary>
/// Tests of the write limit of <see cref="FrameChannel"/>: it covers the writing of a frame, not its wait in the
/// queue, and a closed channel lets nobody wait for ever.
/// </summary>
public sealed class FrameChannelTimeoutTests
{
    /// <summary>
    /// A frame queued behind a slow one is not given up on while it waits: its time starts when its turn comes.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task QueuedFrame_IsNotTimedOutWhileItWaits()
    {
        var token = TestContext.Current.CancellationToken;
        var stream = new GatedStream();
        await using var channel = new FrameChannel(stream);
        var slow = channel.SendAsync(new byte[] { 1 }, Timeout.InfiniteTimeSpan, token).AsTask();
        var queued = channel.SendAsync(new byte[] { 2 }, TimeSpan.FromMilliseconds(200), token).AsTask();

        // Far longer than the limit of the queued frame, spent waiting for its turn.
        await Task.Delay(TimeSpan.FromMilliseconds(700), token);
        Assert.False(queued.IsCompleted);

        stream.Open();
        await slow.WaitAsync(TimeSpan.FromSeconds(10), token);
        await queued.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    /// <summary>
    /// A write that does not finish within its limit is abandoned.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task StuckWrite_IsAbandonedAfterItsLimit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var channel = new FrameChannel(new GatedStream());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.SendAsync(new byte[] { 1 }, TimeSpan.FromMilliseconds(100), token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token));
    }

    /// <summary>
    /// Closing the channel releases the writers that wait for their turn, and later writers are refused at once.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task ClosedChannel_ReleasesWaitingWriters()
    {
        var token = TestContext.Current.CancellationToken;
        var channel = new FrameChannel(new GatedStream());
        var stuck = channel.SendAsync(new byte[] { 1 }, Timeout.InfiniteTimeSpan, token).AsTask();
        var waiting = channel.SendAsync(new byte[] { 2 }, Timeout.InfiniteTimeSpan, token).AsTask();

        await channel.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => stuck.WaitAsync(TimeSpan.FromSeconds(10), token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10), token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.SendAsync(new byte[] { 3 }, token).AsTask());
    }

    /// <summary>
    /// A stream whose writes do not finish until it is opened, like a connection the other side does not read from.
    /// </summary>
    private sealed class GatedStream : Stream
    {
        /// <summary>
        /// Completed when writes may pass.
        /// </summary>
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Lets the writes pass.
        /// </summary>
        public void Open() => _opened.TrySetResult();

        /// <inheritdoc />
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => await _opened.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        /// <inheritdoc />
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
