namespace Golether.Media.Streaming.Caching;

/// <summary>
/// An in-memory least-recently-used cache of chunks bounded by size.
/// </summary>
/// <remarks>
/// Received media is kept in memory only and disappears when the session ends. The class is thread-safe.
/// </remarks>
public sealed class ChunkCache
{
    /// <summary>
    /// The default capacity: 1 GiB.
    /// </summary>
    public const long DefaultCapacity = 1024L * 1024 * 1024;

    /// <summary>
    /// Chunks by index.
    /// </summary>
    private readonly Dictionary<long, LinkedListNode<(long Index, byte[] Data)>> _entries = [];

    /// <summary>
    /// Chunks from the most to the least recently used.
    /// </summary>
    private readonly LinkedList<(long Index, byte[] Data)> _recency = new();

    /// <summary>
    /// Guards the cache.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The number of cached bytes.
    /// </summary>
    private long _size;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkCache"/> class.
    /// </summary>
    /// <param name="capacity">The capacity in bytes.</param>
    public ChunkCache(long capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>
    /// Gets the capacity in bytes.
    /// </summary>
    public long Capacity { get; }

    /// <summary>
    /// Gets the number of cached bytes.
    /// </summary>
    public long Size
    {
        get
        {
            lock (_gate)
            {
                return _size;
            }
        }
    }

    /// <summary>
    /// Returns a chunk and marks it as recently used.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <param name="data">The chunk data.</param>
    /// <returns><see langword="true"/> when the chunk is cached.</returns>
    public bool TryGet(long index, out byte[] data)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(index, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                data = node.Value.Data;
                return true;
            }
        }

        data = [];
        return false;
    }

    /// <summary>
    /// Returns a cached chunk without changing its recency (for serving other participants).
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <returns>The chunk, or <see langword="null"/>.</returns>
    public byte[]? Peek(long index)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(index, out var node) ? node.Value.Data : null;
        }
    }

    /// <summary>
    /// Lists the cached chunks as ranges of consecutive indexes, the longest first.
    /// </summary>
    /// <param name="maxRanges">The largest number of ranges.</param>
    /// <returns>The ranges.</returns>
    public IReadOnlyList<(long Start, long Count)> GetRanges(int maxRanges)
    {
        long[] indexes;
        lock (_gate)
        {
            indexes = [.. _entries.Keys];
        }

        Array.Sort(indexes);
        var ranges = new List<(long Start, long Count)>();
        for (var i = 0; i < indexes.Length;)
        {
            var start = indexes[i];
            var j = i + 1;
            while (j < indexes.Length && indexes[j] == indexes[j - 1] + 1)
            {
                j++;
            }

            ranges.Add((start, j - i));
            i = j;
        }

        return [.. ranges.OrderByDescending(r => r.Count).Take(maxRanges)];
    }

    /// <summary>
    /// Determines whether a chunk is cached without changing its recency.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <returns><see langword="true"/> when the chunk is cached.</returns>
    public bool Contains(long index)
    {
        lock (_gate)
        {
            return _entries.ContainsKey(index);
        }
    }

    /// <summary>
    /// Adds or replaces a chunk and evicts the least recently used chunks above the capacity.
    /// </summary>
    /// <param name="index">The chunk index.</param>
    /// <param name="data">The chunk data.</param>
    public void Add(long index, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            if (_entries.Remove(index, out var existing))
            {
                _recency.Remove(existing);
                _size -= existing.Value.Data.Length;
            }

            _entries[index] = _recency.AddFirst((index, data));
            _size += data.Length;
            while (_size > Capacity && _recency.Last is { } last && last != _recency.First)
            {
                _recency.RemoveLast();
                _entries.Remove(last.Value.Index);
                _size -= last.Value.Data.Length;
            }
        }
    }

    /// <summary>
    /// Counts consecutive cached chunks starting at an index.
    /// </summary>
    /// <param name="index">The first chunk index.</param>
    /// <param name="limit">The maximum count.</param>
    /// <returns>The number of consecutive cached chunks.</returns>
    public long CountContiguous(long index, long limit)
    {
        lock (_gate)
        {
            var count = 0L;
            while (count < limit && _entries.ContainsKey(index + count))
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Removes all chunks.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
            _size = 0;
        }
    }
}
