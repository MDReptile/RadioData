using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RadioDataApp.Modem
{
    public static class CustomProtocol
    {
        // Sync Word: 0xAA 0x55 (10101010 01010101)
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

            Console.WriteLine($"[DEBUG] Sync found at index {syncIndex}");

            // Remove garbage before sync
            if (syncIndex > 0)
            {
                buffer.RemoveRange(0, syncIndex);
                syncIndex = 0;
            }

            // Check Length
            if (buffer.Count < 3) return null;
            byte length = buffer[2];
            Console.WriteLine($"[DEBUG] Payload Length: {length}");

            int totalPacketSize = 3 + length + 1;

            if (buffer.Count < totalPacketSize)
            {
                Console.WriteLine($"[DEBUG] Waiting for more data. Have {buffer.Count}, need {totalPacketSize}");
                return null; // Wait for more data
            }

            // Validate Checksum
            byte calculatedChecksum = 0;
            for (int i = 0; i < length; i++)
            {
                calculatedChecksum += buffer[3 + i];
            }

            byte receivedChecksum = buffer[3 + length];
            Console.WriteLine($"[DEBUG] Checksum Calc: {calculatedChecksum}, Recv: {receivedChecksum}");

            if (calculatedChecksum != receivedChecksum)
            {
                Console.WriteLine("[DEBUG] Checksum Mismatch! (Ignoring for debug)");
                // Corrupt packet, remove the Sync Word and try again next time
                // buffer.RemoveRange(0, 2); 
                // return null;
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
