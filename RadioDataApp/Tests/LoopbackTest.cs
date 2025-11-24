using RadioDataApp.Modem;
using RadioDataApp.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RadioDataApp.Tests
{
    public class LoopbackTest
    {
        private static void Log(string message)
        {
            string output = $"[SYSTEM LOG] {message}";
            Console.WriteLine(output);
            Debug.WriteLine(output);
        }

        public static void RunTests()
        {
            Log("========================================");
            Log("RadioData Loopback Tests Starting");
            Log("========================================");
            Log("");

            try
            {
                TestTextMessage();
                TestSmallFile();
                TestEncryption();
                TestMultipleMessages();

                Log("");
                Log("========================================");
                Log("[SUCCESS] All Tests Complete");
                Log("========================================");
            }
            catch (Exception ex)
            {
                Log("");
                Log("========================================");
                Log($"[FAILED] Test Suite Failed: {ex.Message}");
                Log($"Stack: {ex.StackTrace}");
                Log("========================================");
            }
        }

        public static async Task RunAllTestsIncludingAudio(int outputDevice = 0, int inputDevice = 0)
        {
            Log("========================================");
            Log("RadioData FULL Test Suite");
            Log("========================================");
            Log("Running: Memory Tests + Audio Tests");
            Log("");

            try
            {
                // Run memory-based tests
                RunTests();

                Log("");
                Log("Memory tests complete. Starting audio tests...");
                Log("");

                // Run audio loopback test
                await AudioLoopbackTest.RunAudioLoopbackTest(outputDevice, inputDevice);

                Log("");
                Log("========================================");
                Log("[SUCCESS] FULL Test Suite Complete");
                Log("========================================");
            }
            catch (Exception ex)
            {
                Log("");
                Log("========================================");
                Log($"[FAILED] Full Test Suite Failed: {ex.Message}");
                Log($"Stack: {ex.StackTrace}");
                Log("========================================");
            }
        }

        private static void TestTextMessage()
        {
            Log("----------------------------------------");
            Log("Test 1: Text Message Transmission");
            Log("----------------------------------------");

            var stopwatch = Stopwatch.StartNew();
            var modem = new AfskModem();
            string testMessage = "Hello RadioData - Loopback Test!";

            Log($"Original message: \"{testMessage}\" ({testMessage.Length} chars)");

            // Encode
            byte[] packet = CustomProtocol.Encode(testMessage);
            Log($"[OK] Protocol encoded: {packet.Length} bytes");
            Log($"  Packet hex: {BitConverter.ToString(packet).Replace("-", " ")}");

            byte[] audio = modem.Modulate(packet);
            Log($"[OK] Modulated to audio: {audio.Length} bytes ({audio.Length / 88.2f:F1}ms at 44.1kHz)");

            // Decode
            Log("Starting demodulation...");
            var decodedPacket = modem.Demodulate(audio);

            if (decodedPacket != null && decodedPacket.Type == CustomProtocol.PacketType.Text)
            {
                string received = Encoding.ASCII.GetString(decodedPacket.Payload);
                bool success = received == testMessage;

                Log($"[OK] Decoded packet type: {decodedPacket.Type}");
                Log($"[OK] Decoded message: \"{received}\"");
                Log($"[OK] Payload length: {decodedPacket.Payload.Length} bytes");

                stopwatch.Stop();
                Log($"[TIME] Round-trip time: {stopwatch.ElapsedMilliseconds}ms");

                if (success)
                {
                    Log("[PASS] Message matches perfectly!");
                }
                else
                {
                    Log($"[FAIL] Message mismatch!");
                    Log($"  Expected: \"{testMessage}\"");
                    Log($"  Got:      \"{received}\"");
                }
            }
            else
            {
                Log($"[FAIL] Could not decode message (packet={decodedPacket?.Type.ToString() ?? "null"})");
            }

            Log("");
        }

        private static void TestSmallFile()
        {
            Log("----------------------------------------");
            Log("Test 2: Small File Transfer");
            Log("----------------------------------------");

            var stopwatch = Stopwatch.StartNew();
            var modem = new AfskModem();
            var fileService = new FileTransferService();

            // Create test data
            byte[] testData = Encoding.ASCII.GetBytes("This is test file content for RadioData transmission testing. Lorem ipsum dolor sit amet.");
            string tempFile = Path.Combine(Path.GetTempPath(), "radiodata_test.txt");
            File.WriteAllBytes(tempFile, testData);

            Log($"[OK] Created test file: {testData.Length} bytes");
            Log($"  Path: {tempFile}");

            // Prepare packets
            var packets = fileService.PrepareFileForTransmission(tempFile);
            Log($"[OK] Generated {packets.Count} packets:");
            Log($"  - 1 header packet");
            Log($"  - {packets.Count - 1} data chunk(s)");

            // Simulate transmission/reception
            int decodedPackets = 0;
            int headerPackets = 0;
            int chunkPackets = 0;

            foreach (var packet in packets)
            {
                byte[] audio = modem.Modulate(packet);
                var decoded = modem.Demodulate(audio);

                if (decoded != null)
                {
                    decodedPackets++;

                    if (decoded.Type == CustomProtocol.PacketType.FileHeader)
                    {
                        headerPackets++;
                        Log($"  [OK] Decoded header packet ({decoded.Payload.Length} bytes)");
                    }
                    else if (decoded.Type == CustomProtocol.PacketType.FileChunk)
                    {
                        chunkPackets++;
                        Log($"  [OK] Decoded chunk packet {chunkPackets} ({decoded.Payload.Length} bytes)");
                    }

                    fileService.HandlePacket(decoded);
                }
                else
                {
                    Log($"  [WARN] Failed to decode packet");
                }
            }

            stopwatch.Stop();
            Log($"[OK] Decoded {decodedPackets}/{packets.Count} packets");
            Log($"  - Headers: {headerPackets}");
            Log($"  - Chunks: {chunkPackets}");
            Log($"[TIME] Total time: {stopwatch.ElapsedMilliseconds}ms");

            if (decodedPackets == packets.Count)
            {
                Log("[PASS] All packets decoded successfully!");
            }
            else
            {
                Log($"[FAIL] Packet loss detected! ({packets.Count - decodedPackets} lost)");
            }

            // Cleanup
            if (File.Exists(tempFile))
                File.Delete(tempFile);

            Log("");
        }

        private static void TestEncryption()
        {
            Log("----------------------------------------");
            Log("Test 3: Encryption/Decryption");
            Log("----------------------------------------");

            var modem = new AfskModem();
            string message = "Secret Message 123!";
            string originalKey = CustomProtocol.EncryptionKey;

            Log($"Original message: \"{message}\"");
            Log($"Testing with key: \"TEST1\"");

            // Test with key "TEST1"
            CustomProtocol.EncryptionKey = "TEST1";
            byte[] packet1 = CustomProtocol.Encode(message);
            byte[] audio1 = modem.Modulate(packet1);

            Log($"[OK] Encrypted and modulated: {audio1.Length} bytes");

            // Try to decode with wrong key
            CustomProtocol.EncryptionKey = "WRONG";
            var decoded1 = modem.Demodulate(audio1);
            string wrongKey = decoded1 != null ? Encoding.ASCII.GetString(decoded1.Payload) : "[null]";
            Log($"With WRONG key, decoded: \"{wrongKey}\"");

            // Decode with correct key
            CustomProtocol.EncryptionKey = "TEST1";
            var decoded2 = modem.Demodulate(audio1);
            string correctKey = decoded2 != null ? Encoding.ASCII.GetString(decoded2.Payload) : "[null]";
            Log($"With CORRECT key, decoded: \"{correctKey}\"");

            bool encrypted = wrongKey != message;
            bool decrypted = correctKey == message;

            if (encrypted && decrypted)
            {
                Log("[PASS] Encryption working correctly!");
                Log("  [OK] Wrong key produces garbled output");
                Log("  [OK] Correct key restores original message");
            }
            else
            {
                Log("[FAIL] Encryption issue detected!");
                if (!encrypted)
                    Log("  [FAIL] Wrong key should NOT decode correctly");
                if (!decrypted)
                    Log("  [FAIL] Correct key should decode correctly");
            }

            // Reset to default
            CustomProtocol.EncryptionKey = originalKey;
            Log($"Restored encryption key to: \"{originalKey}\"");
            Log("");
        }

        private static void TestMultipleMessages()
        {
            Log("----------------------------------------");
            Log("Test 4: Multiple Sequential Messages");
            Log("----------------------------------------");

            var modem = new AfskModem();
            string[] messages = { "First", "Second", "Third Message!" };
            int successCount = 0;

            for (int i = 0; i < messages.Length; i++)
            {
                byte[] packet = CustomProtocol.Encode(messages[i]);
                byte[] audio = modem.Modulate(packet);
                var decoded = modem.Demodulate(audio);

                if (decoded != null && decoded.Type == CustomProtocol.PacketType.Text)
                {
                    string received = Encoding.ASCII.GetString(decoded.Payload);
                    bool match = received == messages[i];

                    if (match)
                    {
                        successCount++;
                        Log($"  [OK] Message {i + 1}: \"{messages[i]}\" -> \"{received}\" [PASS]");
                    }
                    else
                    {
                        Log($"  [FAIL] Message {i + 1}: Expected \"{messages[i]}\", got \"{received}\" [FAIL]");
                    }
                }
                else
                {
                    Log($"  [FAIL] Message {i + 1}: Decode failed [FAIL]");
                }
            }

            if (successCount == messages.Length)
            {
                Log($"[PASS] All {messages.Length} messages transmitted correctly!");
            }
            else
            {
                Log($"[FAIL] Only {successCount}/{messages.Length} messages succeeded");
            }

            Log("");
        }
    }
}
