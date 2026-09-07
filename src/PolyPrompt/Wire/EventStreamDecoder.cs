namespace PolyPrompt.Wire
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Decoder for the AWS <c>application/vnd.amazon.eventstream</c> binary framing used by Bedrock's
    /// <c>ConverseStream</c> / <c>InvokeModelWithResponseStream</c> responses. Unlike the SSE/NDJSON streams
    /// every other provider uses, this is a length-prefixed binary protocol: each message is a 12-byte
    /// prelude (total length, headers length, prelude CRC-32), a headers block, a payload, and a trailing
    /// message CRC-32. Both CRCs are validated to detect stream desynchronization.
    /// </summary>
    public static class EventStreamDecoder
    {
        #region Public-Methods

        /// <summary>
        /// Asynchronously decode a stream of AWS event-stream messages until the underlying stream ends.
        /// </summary>
        /// <param name="stream">The response body stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An async sequence of decoded messages.</returns>
        /// <exception cref="InvalidDataException">Thrown when a CRC check fails or a frame is truncated.</exception>
        public static async IAsyncEnumerable<EventStreamMessage> DecodeAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                byte[]? prelude = await ReadExactAsync(stream, 12, allowEof: true, token).ConfigureAwait(false);
                if (prelude == null) yield break; // clean end of stream

                uint totalLength = ReadUInt32BigEndian(prelude, 0);
                uint headersLength = ReadUInt32BigEndian(prelude, 4);
                uint preludeCrc = ReadUInt32BigEndian(prelude, 8);

                uint computedPreludeCrc = Crc32.Compute(prelude, 0, 8);
                if (computedPreludeCrc != preludeCrc)
                    throw new InvalidDataException("AWS event-stream prelude CRC mismatch (stream desynchronized).");

                if (totalLength < 16 || headersLength > totalLength - 16)
                    throw new InvalidDataException("AWS event-stream message declared an invalid length.");

                int remaining = (int)totalLength - 12;
                byte[] rest = await ReadExactAsync(stream, remaining, allowEof: false, token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("AWS event-stream message was truncated.");

                // The message CRC covers the entire message (prelude + headers + payload), i.e. everything
                // except the trailing 4 CRC bytes themselves.
                int messageBodyLength = remaining - 4;
                uint messageCrc = ReadUInt32BigEndian(rest, messageBodyLength);
                uint computedMessageCrc = Crc32.Compute(prelude, 0, 12);
                computedMessageCrc = Crc32.Update(computedMessageCrc, rest, 0, messageBodyLength);
                if (computedMessageCrc != messageCrc)
                    throw new InvalidDataException("AWS event-stream message CRC mismatch (corrupt frame).");

                int headersEnd = (int)headersLength;
                Dictionary<string, string> headers = ParseHeaders(rest, 0, headersEnd);

                byte[] payload = new byte[messageBodyLength - headersEnd];
                Array.Copy(rest, headersEnd, payload, 0, payload.Length);

                yield return new EventStreamMessage(headers, payload);
            }
        }

        /// <summary>
        /// Encode a single event-stream message: prelude (with CRC), string-valued headers, payload, and the
        /// trailing message CRC. Provided so tests (and any producer) can generate byte-accurate frames the
        /// decoder round-trips.
        /// </summary>
        /// <param name="headers">String-valued headers (e.g. <c>:event-type</c>, <c>:content-type</c>).</param>
        /// <param name="payload">The message payload bytes.</param>
        /// <returns>The fully-framed message bytes.</returns>
        public static byte[] EncodeMessage(IReadOnlyDictionary<string, string> headers, byte[] payload)
        {
            headers ??= new Dictionary<string, string>();
            payload ??= Array.Empty<byte>();

            using MemoryStream headerStream = new MemoryStream();
            foreach (KeyValuePair<string, string> header in headers)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(header.Key);
                if (nameBytes.Length > byte.MaxValue)
                    throw new ArgumentException("Event-stream header name too long: " + header.Key);
                byte[] valueBytes = Encoding.UTF8.GetBytes(header.Value ?? string.Empty);

                headerStream.WriteByte((byte)nameBytes.Length);
                headerStream.Write(nameBytes, 0, nameBytes.Length);
                headerStream.WriteByte(7); // value type 7 = string
                headerStream.WriteByte((byte)((valueBytes.Length >> 8) & 0xFF));
                headerStream.WriteByte((byte)(valueBytes.Length & 0xFF));
                headerStream.Write(valueBytes, 0, valueBytes.Length);
            }

            byte[] headerBytes = headerStream.ToArray();
            int totalLength = 12 + headerBytes.Length + payload.Length + 4;

            byte[] message = new byte[totalLength];
            WriteUInt32BigEndian(message, 0, (uint)totalLength);
            WriteUInt32BigEndian(message, 4, (uint)headerBytes.Length);
            uint preludeCrc = Crc32.Compute(message, 0, 8);
            WriteUInt32BigEndian(message, 8, preludeCrc);

            Array.Copy(headerBytes, 0, message, 12, headerBytes.Length);
            Array.Copy(payload, 0, message, 12 + headerBytes.Length, payload.Length);

            int bodyLength = totalLength - 4;
            uint messageCrc = Crc32.Compute(message, 0, bodyLength);
            WriteUInt32BigEndian(message, bodyLength, messageCrc);

            return message;
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string> ParseHeaders(byte[] buffer, int offset, int end)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.Ordinal);
            int pos = offset;

            while (pos < end)
            {
                int nameLength = buffer[pos++];
                if (pos + nameLength > end) break;
                string name = Encoding.UTF8.GetString(buffer, pos, nameLength);
                pos += nameLength;

                if (pos >= end) break;
                int valueType = buffer[pos++];

                // Bedrock uses only string-valued (type 7) headers. Other types are skipped defensively so a
                // future header addition does not derail decoding.
                if (valueType == 7)
                {
                    if (pos + 2 > end) break;
                    int valueLength = (buffer[pos] << 8) | buffer[pos + 1];
                    pos += 2;
                    if (pos + valueLength > end) break;
                    string value = Encoding.UTF8.GetString(buffer, pos, valueLength);
                    pos += valueLength;
                    headers[name] = value;
                }
                else
                {
                    pos = SkipHeaderValue(buffer, pos, end, valueType);
                    if (pos < 0) break;
                }
            }

            return headers;
        }

        private static int SkipHeaderValue(byte[] buffer, int pos, int end, int valueType)
        {
            switch (valueType)
            {
                case 0:
                case 1:
                    return pos; // bool true / false: no value bytes
                case 2:
                    return pos + 1; // byte
                case 3:
                    return pos + 2; // short
                case 4:
                    return pos + 4; // int
                case 5:
                case 8:
                    return pos + 8; // long / timestamp
                case 6: // bytes: 2-byte length prefix
                    if (pos + 2 > end) return -1;
                    return pos + 2 + ((buffer[pos] << 8) | buffer[pos + 1]);
                case 9:
                    return pos + 16; // uuid
                default:
                    return -1;
            }
        }

        private static async Task<byte[]?> ReadExactAsync(Stream stream, int count, bool allowEof, CancellationToken token)
        {
            if (count == 0) return Array.Empty<byte>();

            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), token).ConfigureAwait(false);
                if (n == 0)
                {
                    if (read == 0 && allowEof) return null; // clean EOF on a message boundary
                    throw new InvalidDataException("AWS event-stream ended mid-message.");
                }
                read += n;
            }

            return buffer;
        }

        private static uint ReadUInt32BigEndian(byte[] buffer, int offset)
        {
            return (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);
        }

        private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        #endregion
    }

    /// <summary>
    /// A single decoded AWS event-stream message: its string-valued headers and raw payload.
    /// </summary>
    public sealed class EventStreamMessage
    {
        /// <summary>The string-valued message headers (e.g. <c>:event-type</c>, <c>:message-type</c>).</summary>
        public Dictionary<string, string> Headers { get; }

        /// <summary>The raw payload bytes (for Bedrock, a JSON document).</summary>
        public byte[] Payload { get; }

        /// <summary>The value of the <c>:event-type</c> header, or null when absent.</summary>
        public string? EventType => Headers.TryGetValue(":event-type", out string? value) ? value : null;

        /// <summary>The value of the <c>:exception-type</c> header, or null when absent.</summary>
        public string? ExceptionType => Headers.TryGetValue(":exception-type", out string? value) ? value : null;

        /// <summary>The payload decoded as a UTF-8 string.</summary>
        public string PayloadString => Encoding.UTF8.GetString(Payload);

        /// <summary>Initialize a decoded message.</summary>
        /// <param name="headers">The message headers.</param>
        /// <param name="payload">The message payload.</param>
        public EventStreamMessage(Dictionary<string, string> headers, byte[] payload)
        {
            Headers = headers ?? new Dictionary<string, string>(StringComparer.Ordinal);
            Payload = payload ?? Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Standard CRC-32 (IEEE 802.3 polynomial, reflected) as required by the AWS event-stream framing.
    /// </summary>
    internal static class Crc32
    {
        private static readonly uint[] _Table = BuildTable();

        public static uint Compute(byte[] buffer, int offset, int length)
        {
            return Update(0u, buffer, offset, length);
        }

        public static uint Update(uint runningCrc, byte[] buffer, int offset, int length)
        {
            uint crc = runningCrc ^ 0xFFFFFFFFu;
            for (int i = 0; i < length; i++)
            {
                byte b = buffer[offset + i];
                crc = _Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }

            return table;
        }
    }
}
