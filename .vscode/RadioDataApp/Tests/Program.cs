using System;
using System.Text;
using System.Collections.Generic;
using RadioDataApp.Modem;

namespace RadioDataApp.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Testing AfskModem with CustomProtocol...");
            var modem = new AfskModem();
            string message = "Hello World";

            Console.WriteLine($"Modulating: {message}");
            // Use Custom Protocol
            byte[] packet = CustomProtocol.Encode(message);
            Console.WriteLine($"Encoded Packet Length: {packet.Length}");

            byte[] audio = modem.Modulate(packet);
            Console.WriteLine($"Generated {audio.Length} bytes of audio.");

            Console.WriteLine("Demodulating...");
            // Simulate streaming by feeding small chunks
            List<byte> receivedBytes = new List<byte>();
            int chunkSize = 1024;
            for (int i = 0; i < audio.Length; i += chunkSize)
            {
                int length = Math.Min(chunkSize, audio.Length - i);
                byte[] chunk = new byte[length];
                Array.Copy(audio, i, chunk, 0, length);

                byte[]? decodedChunk = modem.Demodulate(chunk);
                if (decodedChunk != null)
                {
                    receivedBytes.AddRange(decodedChunk);
                }
            }

            string receivedString = Encoding.ASCII.GetString(receivedBytes.ToArray());
            Console.WriteLine($"Decoded: {receivedString}");

            if (receivedString == message)
            {
                Console.WriteLine("SUCCESS: Message matches!");
            }
            else
            {
                Console.WriteLine("FAILURE: Message mismatch.");
            }
        }
    }
}
