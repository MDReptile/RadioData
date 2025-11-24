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

        public static byte[] Encode(string text)
        {
            byte[] payload = Encoding.ASCII.GetBytes(text);
            List<byte> packet = [];

            // 1. Sync Word
            packet.AddRange(SyncWord);

            // 2. Length (1 byte)
            if (payload.Length > 255) throw new ArgumentException("Message too long");
            packet.Add((byte)payload.Length);

            // 3. Payload (Obfuscated)
            foreach (byte b in payload)
            {
                packet.Add((byte)(b ^ XorKey));
            }

            // 4. Checksum (Simple Sum of payload)
            byte checksum = 0;
            foreach (byte b in payload) checksum += b; // Sum of ORIGINAL bytes or OBFUSCATED? Let's do Obfuscated for easier checking

            // Re-calculating checksum on obfuscated data
            checksum = 0;
            for (int i = 3; i < packet.Count; i++)
            {
                checksum += packet[i];
            }
            packet.Add(checksum);

            return packet.ToArray();
        }

        public static string? TryDecode(List<byte> receivedBytes)
        {
            // Search for Sync Word
            // We need at least Sync(2) + Len(1) + Checksum(1) = 4 bytes
            if (receivedBytes.Count < 4) return null;

            // Find Sync Word
            int syncIndex = -1;
            for (int i = 0; i < receivedBytes.Count - 1; i++)
            {
                if (receivedBytes[i] == SyncWord[0] && receivedBytes[i + 1] == SyncWord[1])
                {
                    syncIndex = i;
                    break;
                }
            }

            if (syncIndex == -1) return null;

            // Check if we have enough bytes for Length
            if (syncIndex + 2 >= receivedBytes.Count) return null; // Need Length byte

            byte length = receivedBytes[syncIndex + 2];
            int totalPacketSize = 2 + 1 + length + 1; // Sync + Len + Payload + Checksum

            if (syncIndex + totalPacketSize > receivedBytes.Count) return null; // Incomplete packet

            // Validate Checksum
            byte calculatedChecksum = 0;
            for (int i = 0; i < length; i++)
            {
                calculatedChecksum += receivedBytes[syncIndex + 3 + i];
            }

            byte receivedChecksum = receivedBytes[syncIndex + 3 + length];

            if (calculatedChecksum != receivedChecksum)
            {
                // Bad checksum, remove sync word and try again? 
                // For now, just return null and let the caller handle buffer cleanup (not implemented here yet)
                // Actually, we should probably consume the bytes if we found a sync word but failed checksum?
                // Or just return null and let the buffer grow?
                return null;
            }

            // Decode Payload
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = (byte)(receivedBytes[syncIndex + 3 + i] ^ XorKey);
            }

            // Remove processed bytes from the input list?
            // The caller usually handles this, but here we are just "TryDecode".
            // Let's return the string and let the caller clear the buffer up to the end of the packet.

            return Encoding.ASCII.GetString(payload);
        }

        // Helper to remove processed packet from buffer
        public static void RemovePacket(List<byte> buffer, string decodedText)
        {
            // This is tricky without knowing exactly where it was found again.
            // Let's refine TryDecode to return the consumed count or modify the buffer directly.
        }

        public static string? DecodeAndConsume(List<byte> buffer)
        {
            if (buffer.Count < 4) return null;

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
            int totalPacketSize = 3 + length + 1;

            if (buffer.Count < totalPacketSize) return null; // Wait for more data

            // Validate Checksum
            byte calculatedChecksum = 0;
            for (int i = 0; i < length; i++)
            {
                calculatedChecksum += buffer[3 + i];
            }

            byte receivedChecksum = buffer[3 + length];

            if (calculatedChecksum != receivedChecksum)
            {
                // Corrupt packet, remove the Sync Word and try again next time
                buffer.RemoveRange(0, 2);
                return null;
            }

            // Decode
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = (byte)(buffer[3 + i] ^ XorKey);
            }

            // Consume packet
            buffer.RemoveRange(0, totalPacketSize);

            return Encoding.ASCII.GetString(payload);
        }
    }
}
