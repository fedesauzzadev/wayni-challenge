namespace Bcra.Ingestion.Application.ImportExecution;

public readonly record struct SourceLine(ReadOnlyMemory<byte> Line, long NextOffset);

public sealed class OffsetLineReader
{
    /// <summary>Tamaño por defecto del buffer de lectura del stream (128 KiB).</summary>
    public const int BufferSize = 128 * 1024;

    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private int _valid;
    private int _consumed;
    private long _streamOffset;

    public OffsetLineReader(Stream stream, int bufferSize = BufferSize)
        : this(stream, stream.CanSeek ? stream.Position : 0, bufferSize)
    {
    }

    public OffsetLineReader(Stream stream, long streamOffset, int bufferSize = BufferSize)
    {
        _stream = stream;
        _buffer = new byte[Math.Max(1024, bufferSize)];
        _streamOffset = streamOffset;
    }

    public long Position => _streamOffset - _valid + _consumed;

    public async Task<SourceLine?> ReadNextAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var available = _buffer.AsSpan(_consumed, _valid - _consumed);
            var newline = available.IndexOf((byte)'\n');
            if (newline >= 0)
            {
                var length = newline;
                if (length > 0 && _buffer[_consumed + length - 1] == (byte)'\r')
                    length--;

                var line = _buffer.AsMemory(_consumed, length);
                _consumed += newline + 1;
                return new SourceLine(line, Position);
            }

            if (_consumed > 0)
            {
                available.CopyTo(_buffer);
                _valid -= _consumed;
                _consumed = 0;
            }

            if (_valid == _buffer.Length)
                throw new InvalidDataException($"A line exceeds the reader buffer size of {_buffer.Length} bytes.");

            var read = await _stream.ReadAsync(_buffer.AsMemory(_valid), cancellationToken);
            if (read == 0)
            {
                if (_valid == 0)
                    return null;

                var trailing = _buffer.AsMemory(0, _valid);
                _consumed = _valid;
                return new SourceLine(trailing, Position);
            }

            _valid += read;
            _streamOffset += read;
        }
    }
}
