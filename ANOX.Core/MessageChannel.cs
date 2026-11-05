using System;
using System.IO;
using System.Text;

namespace ANOX.Core;

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

    public MessageChannel(Stream stream)
    {
        _stream = stream;
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

    /// <summary>Blocks until a full message line arrives. Returns null when the connection is closed.</summary>
    public string? ReadSerialized()
    {
        return _reader.ReadLine();
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
