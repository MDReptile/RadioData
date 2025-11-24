using System;
using System.Collections.Generic;
using System.Text;

// Simple standalone test for CustomProtocol
class SimpleTest
{
    static void Main()
    {
        Console.WriteLine("=== CustomProtocol Simple Test ===\n");

        string testMessage = "Hello World";
        Console.WriteLine($"Original: {testMessage}");

        // Encode
        byte[] packet = CustomProtocol.Encode(testMessage);
        Console.WriteLine($"Encoded packet: {packet.Length} bytes");
        Console.WriteLine($"Hex: {BitConverter.ToString(packet)}\n");

        // Decode
        List<byte> buffer = new List<byte>(packet);
        var decoded = CustomProtocol.DecodeAndConsume(buffer);

        if (decoded != null && decoded.Type == CustomProtocol.PacketType.Text)
        {
            string result = Encoding.ASCII.GetString(decoded.Payload);
            Console.WriteLine($"Decoded: {result}");

            if (result == testMessage)
            {
                Console.WriteLine("\n✓ SUCCESS: Protocol working correctly!");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine($"\n✗ FAILURE: Got '{result}'");
                Environment.Exit(1);
            }
        }
        else
        {
            Console.WriteLine("\n✗ FAILURE: Decode returned null or wrong type");
            Environment.Exit(1);
        }
    }
}

// Minimal CustomProtocol implementation for testing
public static class CustomProtocol
{
    private static readonly byte[] SyncWord = { 0xAA, 0x55 };
    private const byte XorKey = 0x42;

    public enum PacketType : byte
    {
        Text = 0x01,
        FileHeader = 0x02,
        FileChunk = 0x03
    }

    public class DecodedPacket
    {
        public PacketType Type { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }

    public static byte[] Encode(string text)
    {
        return Encode(Encoding.ASCII.GetBytes(text), PacketType.Text);
    }

    public static byte[] Encode(byte[] payload, PacketType type)
    {
        List<byte> packet = new List<byte>();
        packet.AddRange(SyncWord);
        packet.Add((byte)payload.Length);
        packet.Add((byte)type);

        foreach (byte b in payload)
        {
            packet.Add((byte)(b ^ XorKey));
        }

        byte checksum = 0;
        for (int i = 3; i < packet.Count; i++)
        {
            checksum += packet[i];
        }
        packet.Add(checksum);

        return packet.ToArray();
    }

    public static DecodedPacket? DecodeAndConsume(List<byte> buffer)
    {
        if (buffer.Count < 5) return null;

        int totalPacketSize = 3 + 1 + buffer[2] + 1;
        if (buffer.Count < totalPacketSize) return null;

        byte calculatedChecksum = 0;
        for (int i = 3; i < totalPacketSize - 1; i++)
        {
            calculatedChecksum += buffer[i];
        }

        if (calculatedChecksum != buffer[totalPacketSize - 1])
        {
            return null;
        }

        PacketType type = (PacketType)buffer[3];
        byte length = buffer[2];
        byte[] payload = new byte[length];

        for (int i = 0; i < length; i++)
        {
            payload[i] = (byte)(buffer[4 + i] ^ XorKey);
        }

        buffer.RemoveRange(0, totalPacketSize);
        return new DecodedPacket { Type = type, Payload = payload };
    }
}
