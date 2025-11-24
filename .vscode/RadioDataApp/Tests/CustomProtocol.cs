using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RadioDataApp.Modem
{
    public static class CustomProtocol
    {
        // Sync Word: 0xAA 0x55 (10101010 01010101)
        // Unlikely to occur naturally in random data and distinct from standard flags
        private static readonly byte[] SyncWord = { 0xAA, 0x55 };
        private const byte XorKey = 0x42; // Simple obfuscation key

        public enum PacketType : byte
        {
            Text = 0x01,
            FileHeader = 0x02,
            FileChunk = 0x03
        }

        public class DecodedPacket
        {
            public PacketType Type { get; set; }
            public byte[] Payload { get; set; } = [];
        }

        public static byte[] Encode(byte[] payload, PacketType type)
        {
            List<byte> packet = [];

            // 1. Sync Word
            packet.AddRange(SyncWord);

            // 2. Length (1 byte)
            if (payload.Length > 255) throw new ArgumentException("Message too long");
            packet.Add((byte)payload.Length);

            // 3. Packet Type (1 byte)
            packet.Add((byte)type);

            // 4. Payload (Obfuscated)
            foreach (byte b in payload)
            {
                packet.Add((byte)(b ^ XorKey));
            }

            // 5. Checksum (Simple Sum of payload + type)
            byte checksum = (byte)type;
            foreach (byte b in payload) checksum += b; // Sum of ORIGINAL bytes

            // Re-calculating checksum on obfuscated data + type for receiver convenience
            checksum = 0;
            for (int i = 3; i < packet.Count; i++)
            {
                checksum += packet[i];
            }
            packet.Add(checksum);

            return packet.ToArray();
        }

        public static byte[] Encode(string text)
        {
            return Encode(Encoding.ASCII.GetBytes(text), PacketType.Text);
        }

        public static DecodedPacket? DecodeAndConsume(List<byte> buffer)
        {
            if (buffer.Count < 5) return null; // Sync(2) + Len(1) + Type(1) + Checksum(1)

            // Find Sync Word
            int syncIndex = -1;
            for (int i = 0; i < buffer.Count - 1; i++)
            {
                if (buffer[i] == SyncWord[0] && buffer[i + 1] == SyncWord[1])
                {
                    syncIndex = i;
                    break;
                }
            }

            if (syncIndex == -1)
            {
                // Keep the last byte just in case it's the first half of a sync word
                if (buffer.Count > 0)
                {
                    byte last = buffer[buffer.Count - 1];
                    buffer.Clear();
                    if (last == SyncWord[0]) buffer.Add(last);
                }
                return null;
            }

            // Remove garbage before sync
            if (syncIndex > 0)
            {
                buffer.RemoveRange(0, syncIndex);
                syncIndex = 0;
            }

            // Check Length
            if (buffer.Count < 3) return null;
            byte length = buffer[2];

            int totalPacketSize = 3 + 1 + length + 1; // Sync(2) + Len(1) + Type(1) + Payload(L) + Checksum(1)

            if (buffer.Count < totalPacketSize)
            {
                return null; // Wait for more data
            }

            // Validate Checksum
            byte calculatedChecksum = 0;
            for (int i = 3; i < totalPacketSize - 1; i++) // Sum Type + Obfuscated Payload
            {
                calculatedChecksum += buffer[i];
            }

            byte receivedChecksum = buffer[totalPacketSize - 1];

            if (calculatedChecksum != receivedChecksum)
            {
                Console.WriteLine("[DEBUG] Checksum Mismatch!");
                // Corrupt packet, remove the Sync Word and try again next time
                buffer.RemoveRange(0, 2);
                return null;
            }

            // Decode
            PacketType type = (PacketType)buffer[3];
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = (byte)(buffer[4 + i] ^ XorKey);
            }

            // Consume packet
            buffer.RemoveRange(0, totalPacketSize);

            return new DecodedPacket { Type = type, Payload = payload };
        }
    }
}
