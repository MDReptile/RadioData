using RadioDataApp.Modem;
using RadioDataApp.Services;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioDataApp.Tests
{
    public class AudioLoopbackTest
    {
        private static AudioService? _audioService;
        private static AfskModem? _demodulator;
        private static bool _messageReceived;
        private static string _receivedMessage = "";
        private static readonly object _lockObject = new object();

        private static void Log(string message)
        {
            string output = $"[AUDIO LOOPBACK] {message}";
            Console.WriteLine(output);
            Debug.WriteLine(output);
        }

        public static async Task RunAudioLoopbackTest(int outputDeviceIndex, int inputDeviceIndex)
        {
            Log("========================================");
            Log("Audio Loopback Test Starting");
            Log("========================================");
            Log($"Output Device: Index {outputDeviceIndex}");
            Log($"Input Device: Index {inputDeviceIndex}");
            Log("");

            try
            {
                await TestTextMessageOverAudio(outputDeviceIndex, inputDeviceIndex);
                
                Log("");
                Log("========================================");
                Log("? Audio Loopback Test Complete");
                Log("========================================");
            }
            catch (Exception ex)
            {
                Log("");
                Log("========================================");
                Log($"? Audio Loopback Test Failed: {ex.Message}");
                Log($"Stack: {ex.StackTrace}");
                Log("========================================");
            }
            finally
            {
                _audioService?.Dispose();
            }
        }

        private static async Task TestTextMessageOverAudio(int outputDevice, int inputDevice)
        {
            Log("----------------------------------------");
            Log("Test: Text Message Over Real Audio");
            Log("----------------------------------------");

            var stopwatch = Stopwatch.StartNew();

            // Setup
            _audioService = new AudioService();
            _demodulator = new AfskModem();
            _messageReceived = false;
            _receivedMessage = "";

            string testMessage = "Hello Audio Loopback!";
            Log($"Test message: \"{testMessage}\"");

            // Subscribe to audio input
            _audioService.AudioDataReceived += OnAudioDataReceived;
            _audioService.StartListening(inputDevice);
            Log("? Audio input started (listening)");

            // Give the audio input time to initialize
            await Task.Delay(500);

            // Encode and modulate
            byte[] packet = CustomProtocol.Encode(testMessage);
            Log($"? Encoded to protocol packet: {packet.Length} bytes");

            var modem = new AfskModem();
            byte[] audioData = modem.Modulate(packet);
            Log($"? Modulated to audio: {audioData.Length} bytes ({audioData.Length / 88.2:F1}ms)");

            // Play audio through speakers
            Log("??  Starting audio playback...");
            _audioService.InitializeTransmission(outputDevice);
            _audioService.QueueAudio(audioData);

            // Wait for transmission to complete + processing time
            int maxWaitMs = 5000; // 5 seconds timeout
            int waitedMs = 0;
            int checkIntervalMs = 100;

            Log("? Waiting for reception...");
            while (!_messageReceived && waitedMs < maxWaitMs)
            {
                await Task.Delay(checkIntervalMs);
                waitedMs += checkIntervalMs;

                if (waitedMs % 1000 == 0)
                {
                    Log($"  ... {waitedMs / 1000}s elapsed");
                }
            }

            stopwatch.Stop();

            // Cleanup
            _audioService.StopTransmission();
            _audioService.StopListening();
            _audioService.AudioDataReceived -= OnAudioDataReceived;

            // Results
            Log("");
            Log("--- Results ---");
            Log($"Total time: {stopwatch.ElapsedMilliseconds}ms");
            Log($"Received: {_messageReceived}");

            if (_messageReceived)
            {
                Log($"Decoded message: \"{_receivedMessage}\"");
                
                if (_receivedMessage == testMessage)
                {
                    Log("? PASS: Audio loopback successful!");
                    Log("  ? Message transmitted through speakers");
                    Log("  ? Message captured by microphone");
                    Log("  ? Message decoded correctly");
                }
                else
                {
                    Log($"? FAIL: Message mismatch!");
                    Log($"  Expected: \"{testMessage}\"");
                    Log($"  Got:      \"{_receivedMessage}\"");
                }
            }
            else
            {
                Log("? FAIL: No message received within timeout");
                Log("  Possible causes:");
                Log("  - Audio devices not properly connected");
                Log("  - Volume too low (check system volume)");
                Log("  - Wrong device selected");
                Log("  - Need virtual audio cable for loopback");
            }

            Log("");
        }

        private static void OnAudioDataReceived(object? sender, byte[] audioData)
        {
            lock (_lockObject)
            {
                if (_messageReceived || _demodulator == null)
                    return;

                // Process audio through demodulator
                var packet = _demodulator.Demodulate(audioData);

                if (packet != null && packet.Type == CustomProtocol.PacketType.Text)
                {
                    _receivedMessage = Encoding.ASCII.GetString(packet.Payload);
                    _messageReceived = true;
                    Log($"?? Audio received and decoded!");
                }
            }
        }

        // Convenience method to test with default devices
        public static async Task RunWithDefaultDevices()
        {
            var inputDevices = AudioService.GetInputDevices();
            var outputDevices = AudioService.GetOutputDevices();

            if (inputDevices.Count == 0 || outputDevices.Count == 0)
            {
                Log("? No audio devices found!");
                return;
            }

            Log("Available Input Devices:");
            for (int i = 0; i < inputDevices.Count; i++)
            {
                Log($"  [{i}] {inputDevices[i].ProductName}");
            }

            Log("");
            Log("Available Output Devices:");
            for (int i = 0; i < outputDevices.Count; i++)
            {
                Log($"  [{i}] {outputDevices[i].ProductName}");
            }
            Log("");

            // Use first available devices
            await RunAudioLoopbackTest(0, 0);
        }

        // Method to list all available devices for manual selection
        public static void ListAvailableDevices()
        {
            Log("========================================");
            Log("Available Audio Devices");
            Log("========================================");

            var inputDevices = AudioService.GetInputDevices();
            Log($"Input Devices ({inputDevices.Count} found):");
            for (int i = 0; i < inputDevices.Count; i++)
            {
                Log($"  [{i}] {inputDevices[i].ProductName}");
                Log($"      Channels: {inputDevices[i].Channels}");
            }

            Log("");

            var outputDevices = AudioService.GetOutputDevices();
            Log($"Output Devices ({outputDevices.Count} found):");
            for (int i = 0; i < outputDevices.Count; i++)
            {
                Log($"  [{i}] {outputDevices[i].ProductName}");
                Log($"      Channels: {outputDevices[i].Channels}");
            }

            Log("========================================");
            Log("");
            Log("To test specific devices, use:");
            Log("  AudioLoopbackTest.RunAudioLoopbackTest(outputIndex, inputIndex)");
            Log("");
            Log("For loopback testing, install VB-Audio Cable and use:");
            Log("  Output: CABLE Input");
            Log("  Input: CABLE Output");
            Log("");
        }
    }
}
