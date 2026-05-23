using System;
using System.IO;
using System.Text;

namespace UnityMCP.Utils
{
    /// <summary>
    /// Length-prefixed message framing: a 4-byte big-endian (network order) unsigned
    /// length, followed by that many bytes of UTF-8 JSON. Both ends of the bridge use
    /// this identical framing.
    /// </summary>
    public static class Framing
    {
        public const int MaxMessageSize = 64 * 1024 * 1024; // 64 MB guard rail

        /// <summary>Reads one framed message. Returns null when the peer disconnects cleanly.</summary>
        public static string ReadMessage(Stream stream)
        {
            byte[] header = ReadExactly(stream, 4);
            if (header == null) return null;

            int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (length < 0 || length > MaxMessageSize)
                throw new IOException("Invalid framed message length: " + length);
            if (length == 0) return string.Empty;

            byte[] payload = ReadExactly(stream, length);
            if (payload == null) return null;
            return Encoding.UTF8.GetString(payload);
        }

        /// <summary>Writes one framed message.</summary>
        public static void WriteMessage(Stream stream, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            int length = payload.Length;
            byte[] header = new byte[4]
            {
                (byte)((length >> 24) & 0xFF),
                (byte)((length >> 16) & 0xFF),
                (byte)((length >> 8) & 0xFF),
                (byte)(length & 0xFF),
            };
            stream.Write(header, 0, 4);
            stream.Write(payload, 0, length);
            stream.Flush();
        }

        static byte[] ReadExactly(Stream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0) return null; // connection closed
                offset += read;
            }
            return buffer;
        }
    }
}
