using System;
using System.IO;
using System.Text;

namespace ANOX.Core;

/// <summary>Thrown when an incoming line exceeds the channel's limit. The line is skipped; the stream stays usable.</summary>
public class MessageTooLongException : Exception
{
    public MessageTooLongException(int limit) : base($"Incoming message exceeds {limit} characters.") { }
}

/// <summary>
/// Sends and receives messages over a stream, one message per line.
/// TCP has no message boundaries, so each serialized message ends with '\n'
/// (safe because usernames can't contain line breaks and the text is base64).
/// </summary>
public sealed class MessageChannel : IDisposable
{
    private static readonly Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly object _writeLock = new();
    private readonly int _maxLineLength;
    private readonly StringBuilder _line = new();

    /// <param name="maxLineLength">Longest serialized message accepted, in characters (protects from memory abuse).</param>
    public MessageChannel(Stream stream, int maxLineLength = 1_000_000)
    {
        _stream = stream;
        _maxLineLength = maxLineLength;
        _reader = new StreamReader(stream, _encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    }

    /// <summary>Thread-safe: several threads may send to the same client.</summary>
    public void Send(Message message)
    {
        byte[] bytes = _encoding.GetBytes(MessageParser.SerializeMessage(message) + "\n");

        lock (_writeLock)
        {
            _stream.Write(bytes);
        }
    }

    /// <summary>
    /// Blocks until a full message line arrives. Returns null when the connection is closed.
    /// </summary>
    /// <exception cref="MessageTooLongException">The line was longer than the limit (it has been skipped).</exception>
    public string? ReadSerialized()
    {
        _line.Clear();
        bool tooLong = false;

        while (true)
        {
            int c = _reader.Read();

            if (c == -1) return null;

            if (c == '\n')
            {
                if (tooLong) throw new MessageTooLongException(_maxLineLength);
                return _line.ToString();
            }

            if (tooLong) continue; // skip the rest of an oversized line

            if (_line.Length >= _maxLineLength)
            {
                tooLong = true;
                _line.Clear();
                continue;
            }

            _line.Append((char)c);
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
