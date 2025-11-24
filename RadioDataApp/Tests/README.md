# RadioData Loopback Tests

## Overview

The loopback tests verify send/receive functionality using **two different approaches**:

### 1. **Memory Loopback Tests** (Current default)
Tests run in-memory by modulating data to audio bytes and immediately demodulating them back—**no actual audio playback**.

### 2. **Audio Loopback Tests** (Real audio testing)
Tests the **full audio pipeline** by actually playing audio through speakers and capturing it through the microphone in the same application. This simulates real-world transmission including NAudio drivers, timing, and audio processing.

---

## How to Run

### Method 1: Automatic Memory Tests (Current Setup)
The memory-based tests run **automatically** when you launch the app in DEBUG mode:

1. Press **F5** in Visual Studio to start debugging
2. The app window opens and tests run in the background
3. View results in **Output Window**:
   - Go to: **View ? Output** (or press `Ctrl+Alt+O`)
   - Select **"Debug"** from the "Show output from:" dropdown
   - Look for `[SYSTEM LOG]` entries

Expected output:
```
[SYSTEM LOG] ========================================
[SYSTEM LOG] RadioData Loopback Tests Starting
[SYSTEM LOG] ========================================
[SYSTEM LOG] ----------------------------------------
[SYSTEM LOG] Test 1: Text Message Transmission
[SYSTEM LOG] ----------------------------------------
[SYSTEM LOG] Original message: "Hello RadioData - Loopback Test!" (33 chars)
[SYSTEM LOG] ? Protocol encoded: 38 bytes
[SYSTEM LOG] ? Modulated to audio: 8272 bytes (93.8ms at 44.1kHz)
[SYSTEM LOG] ? PASS: Message matches perfectly!
...
```

### Method 2: Audio Loopback Test (From Code)
To run the **audio loopback test** programmatically:

```csharp
// Run with specific devices
await AudioLoopbackTest.RunAudioLoopbackTest(outputDeviceIndex: 0, inputDeviceIndex: 0);

// Or list devices first
AudioLoopbackTest.ListAvailableDevices();

// Or use defaults
await AudioLoopbackTest.RunWithDefaultDevices();
```

### Method 3: Audio Loopback Test (From ViewModel Command)
Add a button to your UI and bind it to the `RunAudioLoopbackTestCommand`:

```xml
<Button Content="?? Test Audio Loopback" 
        Command="{Binding RunAudioLoopbackTestCommand}"
        Margin="5"/>
```

The test will:
1. Use your currently selected Input/Output devices
2. Play a test message through speakers
3. Capture and decode it through microphone
4. Display results in both Debug Log and System Log

### Method 4: Disable Auto-Run
To disable automatic testing, edit `MainWindow.xaml.cs`:

```csharp
public MainWindow()
{
    InitializeComponent();

    // Comment out or remove these lines:
    // #if DEBUG
    //     Tests.LoopbackTest.RunTests();
    // #endif
}
```

---

## Test Coverage

### ? Memory Loopback Tests (Fast, No Audio Hardware)

#### Test 1: Text Message Transmission
- **Purpose**: Verify basic encode ? modulate ? demodulate ? decode cycle
- **What it tests**:
  - Protocol packet encoding (sync words, length, type, checksum)
  - Audio modulation (AFSK at 1200/2200 Hz)
  - Audio demodulation (zero-crossing detection, UART state machine)
  - Payload integrity (exact string match)
- **Expected result**: Original message matches decoded message

#### Test 2: Small File Transfer
- **Purpose**: Verify multi-packet file transfer with chunking
- **What it tests**:
  - File packetization (header + data chunks)
  - Sequence ID handling
  - FileTransferService packet handling
  - Complete file reconstruction
- **Expected result**: All packets decode successfully

#### Test 3: Encryption/Decryption
- **Purpose**: Verify XOR encryption key system
- **What it tests**:
  - Encryption with custom key ("TEST1")
  - Wrong key produces garbled output
  - Correct key restores original message
  - Key restoration to default ("RADIO")
- **Expected result**: Only correct key decodes properly

#### Test 4: Multiple Sequential Messages
- **Purpose**: Verify modem state handling across transmissions
- **What it tests**:
  - Multiple encode/decode cycles
  - No state pollution between messages
  - Consistent performance
- **Expected result**: All 3 messages transmit correctly

### ? Audio Loopback Test (Real Audio Hardware)

#### Test: Text Message Over Real Audio
- **Purpose**: Verify the **full audio pipeline** with actual playback/capture
- **What it tests**:
  - NAudio WaveOut playback through speakers
  - NAudio WaveIn capture from microphone
  - Audio driver latency and buffering
  - Real-world signal integrity
  - Complete encode ? play ? capture ? decode cycle
- **Expected result**: Message transmitted through speakers, captured by mic, and decoded correctly

**Output prefix**: `[AUDIO LOOPBACK]`

Example success output:
```
[AUDIO LOOPBACK] ========================================
[AUDIO LOOPBACK] Audio Loopback Test Starting
[AUDIO LOOPBACK] ========================================
[AUDIO LOOPBACK] Test message: "Hello Audio Loopback!"
[AUDIO LOOPBACK] ? Encoded to protocol packet: 26 bytes
[AUDIO LOOPBACK] ? Modulated to audio: 5676 bytes (64.4ms)
[AUDIO LOOPBACK] ??  Starting audio playback...
[AUDIO LOOPBACK] ? Waiting for reception...
[AUDIO LOOPBACK] ?? Audio received and decoded!
[AUDIO LOOPBACK] ? PASS: Audio loopback successful!
```

---

## How the Tests Work

### Memory Loopback Architecture
```
Text/File ? Encode() ? Modulate() ? Audio Bytes (in memory)
                                        ?
                                   Demodulate() ? Decode() ? Verify ?
```

**Key Point**: No actual audio playback or recording occurs. The tests operate entirely on byte arrays in RAM.

### Audio Loopback Architecture
```
Text ? Encode() ? Modulate() ? Audio Bytes
                                   ?
                              WaveOut.Play() ? ?? Speakers
                                                    ?
                                               ?? Microphone ? WaveIn.Capture()
                                                    ?
                                               Demodulate() ? Decode() ? Verify ?
```

**Key Point**: Audio physically plays through hardware and is captured, testing the complete real-world pipeline.

### What Memory Tests Prove
- ? Protocol encoding/decoding works correctly
- ? AFSK modulation/demodulation is functional
- ? Encryption keys are applied properly
- ? File chunking and reassembly logic is sound
- ? No data corruption in the pipeline

### What Memory Tests Don't Test
- ? Audio driver compatibility
- ? Speaker/microphone hardware
- ? System volume levels
- ? Real-time audio buffer management
- ? Audio device latency

### What Audio Tests Prove
- ? All of the above from memory tests, PLUS:
- ? NAudio library integration works
- ? Audio devices are properly configured
- ? Volume levels are adequate
- ? No audio clipping or distortion
- ? Timing and buffering are correct

### What Audio Tests Still Don't Test
- ? Real-world radio interference
- ? VOX triggering reliability
- ? Background noise handling over radio
- ? Signal strength variations
- ? Multi-computer scenarios

For those scenarios, you need **physical hardware testing** with actual radios/audio cables.

---

## Audio Loopback Test Requirements

### For Basic Testing (Same Computer)
You can test with built-in mic/speakers, but results may vary:
- **Risk**: Acoustic feedback
- **Risk**: Background noise interference
- **Tip**: Mute system sounds and close other apps
- **Tip**: Adjust volume to 50-70% range

### For Reliable Testing (Virtual Audio Cable)
**Recommended**: Install a virtual audio cable for clean loopback:

1. **Install VB-Audio Cable** (free):
   - Download from: https://vb-audio.com/Cable/
   - Install and reboot if prompted

2. **Configure RadioData**:
   - **Output Device**: Select "CABLE Input"
   - **Input Device**: Select "CABLE Output"

3. **Run test**:
   - Click the test button or call test method
   - Audio routes digitally (no acoustic path)
   - ? Zero noise, zero latency, 100% reliable

### Why Virtual Audio Cable?
- **No acoustic feedback**: Digital routing only
- **No background noise**: Isolated audio path
- **Deterministic**: Same results every time
- **Fast**: No physical sound propagation delay
- **Perfect for CI/CD**: Automated testing possible

---

## Test Output Format

### Memory Tests
All test output uses the `[SYSTEM LOG]` prefix:

- `? PASS` - Test succeeded
- `? FAIL` - Test failed (with details)
- `?` - Individual step completed successfully
- `?` - Individual step failed
- `??` - Timing information
- `??` - Warning (non-critical)

### Audio Tests
All test output uses the `[AUDIO LOOPBACK]` prefix:

- `? PASS` - Audio test succeeded
- `? FAIL` - Audio test failed
- `??` - Audio playback started
- `??` - Audio received
- `?` - Waiting for data

---

## Troubleshooting

### Memory Tests: "No output in Debug window"
- Make sure you're running in **Debug** mode (not Release)
- Check that `#if DEBUG` block in `MainWindow.xaml.cs` is not commented out
- Verify Output window is set to show "Debug" (not "Build")

### Memory Tests: "Tests show FAIL"
This indicates a real bug in the code. Common causes:
- Modem parameters mismatched (e.g., baud rate inconsistency)
- Protocol checksum calculation error
- Encryption logic mismatch
- Buffer management issue

Review the detailed output to see which specific assertion failed.

### Audio Tests: "No message received within timeout"
**Possible causes**:

1. **Wrong devices selected**:
   - Click "List Available Devices" first
   - Verify you're using the correct input/output pair
   - For virtual cable: Output?CABLE Input, Input?CABLE Output

2. **Volume too low**:
   - Check Windows volume mixer (Win+G)
   - Increase output device volume to 50-80%
   - Check input device isn't muted

3. **Virtual audio cable not installed**:
   - Physical mic/speakers can work but are unreliable
   - Install VB-Audio Cable for best results

4. **Audio device in use**:
   - Close other apps using the audio devices
   - Restart the RadioData app

5. **Acoustic feedback** (if using physical speakers/mic):
   - Reduce volume
   - Add distance between speaker and mic
   - Use virtual audio cable instead

### Audio Tests: "Message mismatch"
- Check encryption key matches (should be "RADIO" by default)
- Verify no audio distortion (reduce volume if clipping)
- Check for background noise interference

### Build Errors in Test Files
- Ensure `AfskModem.cs`, `CustomProtocol.cs`, `AudioService.cs`, and `FileTransferService.cs` exist in the main project
- Verify no duplicate classes in the Tests folder
- Check that all using statements are correct
- Run `dotnet clean` and rebuild

---

## Adding New Tests

### Adding a Memory Test
To add a new memory-based test, follow this pattern:

```csharp
private static void TestYourFeature()
{
    Log("----------------------------------------");
    Log("Test 5: Your Feature Name");
    Log("----------------------------------------");

    // Arrange
    var modem = new AfskModem();
    // ... setup

    // Act
    byte[] packet = CustomProtocol.Encode(...);
    byte[] audio = modem.Modulate(packet);
    var decoded = modem.Demodulate(audio);

    // Assert
    if (/* success condition */)
    {
        Log("? PASS: Feature works!");
    }
    else
    {
        Log("? FAIL: Feature broken!");
    }

    Log("");
}
```

Then add the call to `RunTests()`:
```csharp
public static void RunTests()
{
    // ...existing tests...
    TestYourFeature();  // ? Add here
}
```

### Adding an Audio Test
To add a new audio-based test:

```csharp
private static async Task TestYourAudioFeature(int outputDevice, int inputDevice)
{
    Log("----------------------------------------");
    Log("Audio Test: Your Feature");
    Log("----------------------------------------");

    // Setup audio service and demodulator
    using var audioService = new AudioService();
    var demodulator = new AfskModem();
    bool received = false;

    audioService.AudioDataReceived += (s, data) =>
    {
        var packet = demodulator.Demodulate(data);
        if (packet != null)
        {
            // Handle received packet
            received = true;
        }
    };

    audioService.StartListening(inputDevice);
    await Task.Delay(500); // Let audio input stabilize

    // Transmit
    byte[] audioData = /* generate audio */;
    audioService.InitializeTransmission(outputDevice);
    audioService.QueueAudio(audioData);

    // Wait and verify
    await Task.Delay(2000);
    
    if (received)
        Log("? PASS");
    else
        Log("? FAIL");

    audioService.StopListening();
    audioService.StopTransmission();
}
```

---

## Next Steps: Real-World Testing

Once both memory and audio loopback tests pass:

### 1. **Virtual Audio Cable Test** ? (You are here)
- Install VB-Audio Cable or similar
- Set Output ? Cable Input
- Set Input ? Cable Output
- Run audio loopback test
- Verify 100% success rate

### 2. **Same Computer Physical Test**
- Use built-in speakers and microphone
- Test with acoustic path (speaker ? air ? mic)
- Verify robustness to background noise
- Adjust volume levels for optimal reception

### 3. **Two Computer Test**
- Connect via audio cable (3.5mm male-to-male)
- Computer A: Output only
- Computer B: Input only
- Send messages and files between computers
- Verify bidirectional communication

### 4. **Radio Test**
- Connect APRS-K2 or similar cable
- Set radios to same frequency (simplex)
- Enable VOX on transmitting radio
- Send test messages and files
- Monitor for successful reception

### 5. **Field Test**
- Test at various distances
- Test with different terrain (urban, open field, indoors)
- Measure packet loss rates
- Adjust VOX sensitivity and audio levels as needed

---

## Quick Reference

| Test Type | Hardware Required | Speed | Reliability | Use Case |
|-----------|------------------|-------|-------------|----------|
| **Memory Loopback** | None | ? Fast (< 1s) | ?? 100% | Development, CI/CD, unit testing |
| **Audio Loopback (Virtual)** | Virtual Audio Cable | ? Fast (2-5s) | ?? 99% | Audio driver testing, integration |
| **Audio Loopback (Physical)** | Mic + Speakers | ?? Medium (5-10s) | ?? 80-90% | Hardware verification, demos |
| **Cable Test** | Audio cable | ?? Medium (5-10s) | ?? 95% | Two-computer testing |
| **Radio Test** | Radios + cables | ?? Slow (10-30s) | ?? Variable | Real-world deployment |

---

**Questions?** Check the main README.md or review the source code comments in `LoopbackTest.cs` and `AudioLoopbackTest.cs`.
