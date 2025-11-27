using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace RadioDataApp.Modem
{
    public static class CustomProtocol
    {
        // Sync Word: 0xAA 0x55 (10101010 01010101)
        // Unlikely to occur naturally in random data and distinct from standard flags
        private static readonly byte[] SyncWord = { 0xAA, 0x55 };

        // Configurable encryption key (default: "RADIO")
        public static string EncryptionKey { get; set; } = "RADIO";

        // Cache for derived key to avoid re-hashing on every packet
        private static string _cachedKeyString = "";
        private static byte[] _cachedDerivedKey = Array.Empty<byte>();

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
            public string? SenderName { get; set; }
        }

        // Signal for when checksum validation actually failed
        public static event EventHandler? ChecksumValidationFailed;

        private static byte[] DeriveKeyFromPassword(string password)
        {
            // Check cache first
            if (password == _cachedKeyString && _cachedDerivedKey.Length > 0)
            {
                return _cachedDerivedKey;
            }

            // Use SHA256 to derive a 256-bit key from the password
            // This makes even 1-character differences produce completely different keys
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] hash = sha256.ComputeHash(passwordBytes);

                // Cache the result
                _cachedKeyString = password;
                _cachedDerivedKey = hash;

                return hash;
            }
        }

        private static byte[] ApplyEncryption(byte[] data)
        {
            byte[] keyBytes = DeriveKeyFromPassword(EncryptionKey);
            byte[] encrypted = new byte[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                encrypted[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
            }

            return encrypted;
        }

        private static byte[] RemoveEncryption(byte[] data)
        {
            // XOR is symmetric, so decryption is the same as encryption
            return ApplyEncryption(data);
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

            // 4. Payload (Encrypted with user key)
            byte[] encryptedPayload = ApplyEncryption(payload);
            packet.AddRange(encryptedPayload);

            // 5. Checksum (Simple Sum of encrypted payload + type)
            byte checksum = 0;
            for (int i = 3; i < packet.Count; i++)
            {
                checksum += packet[i];
            }
            packet.Add(checksum);

            return packet.ToArray();
        }

        public static byte[] Encode(string text, string? senderName = null)
        {
            // Format for text messages with sender name:
            // [SenderNameLength(1 byte)][SenderName(variable)][Message(rest)]
            List<byte> payload = [];

            if (!string.IsNullOrEmpty(senderName))
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(senderName);
                payload.Add((byte)nameBytes.Length);
                payload.AddRange(nameBytes);
            }
            else
            {
                payload.Add(0); // No sender name
            }

            payload.AddRange(Encoding.ASCII.GetBytes(text));

            return Encode(payload.ToArray(), PacketType.Text);
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
                Services.LogService.Debug("[DEBUG] Checksum Mismatch!");
                // Signal that checksum validation actually failed
                ChecksumValidationFailed?.Invoke(null, EventArgs.Empty);
                // Corrupt packet, remove the Sync Word and try again next time
                buffer.RemoveRange(0, 2);
                return null;
            }

            // Decode
            PacketType type = (PacketType)buffer[3];
            byte[] encryptedPayload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                encryptedPayload[i] = buffer[4 + i];
            }

            byte[] payload = RemoveEncryption(encryptedPayload);

            // Extract sender name for text messages
            string? senderName = null;
            if (type == PacketType.Text && payload.Length > 0)
            {
                int nameLength = payload[0];
                if (nameLength > 0 && payload.Length > nameLength)
                {
                    senderName = Encoding.ASCII.GetString(payload, 1, nameLength);
                    // Remove sender name from payload, leaving only the message
                    byte[] messageOnly = new byte[payload.Length - nameLength - 1];
                    Array.Copy(payload, nameLength + 1, messageOnly, 0, messageOnly.Length);
                    payload = messageOnly;
                }
                else if (nameLength == 0 && payload.Length > 1)
                {
                    // No sender name, remove the length byte
                    byte[] messageOnly = new byte[payload.Length - 1];
                    Array.Copy(payload, 1, messageOnly, 0, messageOnly.Length);
                    payload = messageOnly;
                }
            }

            // Consume packet
            buffer.RemoveRange(0, totalPacketSize);

            return new DecodedPacket { Type = type, Payload = payload, SenderName = senderName };
        }
    }
}
