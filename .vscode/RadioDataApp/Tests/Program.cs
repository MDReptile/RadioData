using System;
using ModemTest;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== AFSK Modem Test with Custom Protocol ===\n");

        var modem = new AfskModem();

        // Test message
        string testMessage = "Hello World";
        Console.WriteLine($"Original: {testMessage}");

        // Encode using CustomProtocol (returns packet with Type=Text)
        byte[] packet = CustomProtocol.Encode(testMessage);
        Console.WriteLine($"Packet size: {packet.Length} bytes");

        // Modulate
        byte[] audioSamples = modem.Modulate(packet);
        Console.WriteLine($"Audio samples: {audioSamples.Length} bytes ({audioSamples.Length / 88.2f:F1}ms at 44.1kHz)\n");

        // Demodulate
        Console.WriteLine("Demodulating...");
        CustomProtocol.DecodedPacket? decodedPacket = modem.Demodulate(audioSamples);

        if (decodedPacket != null)
        {
            if (decodedPacket.Type == CustomProtocol.PacketType.Text)
            {
                string decoded = System.Text.Encoding.ASCII.GetString(decodedPacket.Payload);
                Console.WriteLine($"Decoded: {decoded}");

                if (decoded == testMessage)
                {
                    Console.WriteLine("\nSUCCESS: Message matches!");
                }
                else
                {
                    Console.WriteLine($"\nFAILURE: Expected '{testMessage}', got '{decoded}'");
                }
            }
            else
            {
                Console.WriteLine($"\nFAILURE: Wrong packet type: {decodedPacket.Type}");
            }
        }
        else
        {
            Console.WriteLine("\nFAILURE: Demodulation returned null");
        }
    }
}
