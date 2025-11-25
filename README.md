# RadioData - AFSK Data Transfer Application

## Overview

RadioData is a WPF application for transmitting text messages and files over audio using Audio Frequency Shift Keying (AFSK) modulation. It works with computer speakers/microphones, virtual audio cables, or radio transceivers via audio interfaces.

**Current Version**: .NET 8.0  
**Status**: Working with known asymmetry issues in send/receive reliability

---

## 🔍 Code Analysis & Known Issues

### Critical Issues Identified

#### 1. **Zero-Crossing Detection Asymmetry**
**Location**: `AfskModem.cs` - `ProcessZeroCrossing()` method  
**Problem**: The fixed threshold of **14 samples** for distinguishing Mark (1200 Hz) from Space (2200 Hz) frequencies is hardcoded:

```csharp
if (_samplesSinceCrossing > 14)
{
    _currentLevel = true; // Mark
}
else
{
    _currentLevel = false; // Space
}
```

**Why This Causes Send/Receive Asymmetry**:
- At 44.1 kHz sample rate with 250 baud:
  - **Mark (1200 Hz)**: ~36.75 samples per cycle
  - **Space (2200 Hz)**: ~20.05 samples per cycle
- The threshold of 14 samples is trying to find the midpoint (~18.4 samples)
- However, this threshold is sensitive to:
  - Signal strength (weak signals may have slower zero crossings)
  - Noise levels (can cause premature crossings)
  - Audio device latency differences (input vs output)
  - System audio processing (one device may have more filtering than another)

**Impact**: One computer's audio output may produce slightly different zero-crossing timing than another computer's input captures, causing one direction to work better.

#### 2. **Start Bit Latency Compensation Hardcoded**
**Location**: `AfskModem.cs` - `ProcessUartState()` method  
**Problem**: 
```csharp
case UartState.StartBit:
    if (!_currentLevel) // Detected Space (Start Bit)
    {
        _state = UartState.StartBit;
        _samplesInCurrentState = -2; // ⚠️ Hardcoded compensation
    }
```

**Why This Causes Issues**:
- The `-2` sample compensation assumes a fixed detection delay
- Different audio hardware has different latencies
- This value works well in one direction but may be off in the reverse direction
- Leads to bit timing misalignment and higher error rates in one direction

#### 3. **Baud Rate Mismatch in Documentation**
**Location**: Multiple files  
**Problem**: Code uses **250 baud** but documentation and comments reference **500 baud**
```csharp
private const int BaudRate = 250; // Code says 250
// But README.md and comments reference 500 baud
```
**Impact**: Confusing for developers and users trying to calculate transmission times.

#### 4. **Output Device Index Bug**
**Location**: `MainViewModel.cs` - `OnSelectedOutputDeviceIndexChanged()`  
**Problem**: When loopback mode is enabled at index 0, the device index calculation for real devices is:
```csharp
int deviceIndex = _audioService.IsLoopbackMode ? 0 : SelectedOutputDeviceIndex - 1;
```

However, the loopback check happens in TWO places (input and output selection), and either one can set `IsLoopbackMode = true`, causing confusion when only one is set to loopback while the other isn't.

**Impact**: Can lead to transmitting to wrong device or device index errors.

#### 5. **Signal Clipping with Input Gain**
**Location**: `AfskModem.cs` - `Demodulate()` method  
**Problem**:
```csharp
sample *= InputGain;
sample = Math.Clamp(sample, -1.0f, 1.0f);
```

While clamping prevents overflow, it causes **hard clipping distortion** which destroys zero-crossing timing accuracy.

**Impact**: High input gain can actually make reception worse by distorting the waveform.

#### 6. **Inconsistent Preamble Duration**
**Location**: `MainViewModel.cs` - `SendFile()` method  
**Problem**: Text messages use **1200ms preamble**, but file headers also use **1200ms**. The modulator defaults to **1200ms**.

```csharp
// Text message (implicit 1200ms preamble)
byte[] audioSamples = _modem.Modulate(packet);

// File transmission (explicit 1200ms first packet, 0ms others)
var audio = _modem.Modulate(packets[i], preamble, preambleDuration);
```

**Why This Matters**: 
- 1200ms may be too long for some VOX radios (wastes bandwidth)
- May be too short for others (VOX doesn't trigger reliably)
- No ability to adjust per-device or per-transmission type

---

## 🛠️ Technical Architecture

### Core Components

#### `AfskModem.cs` - AFSK Modulator/Demodulator
**Modulation**:
- **Baud Rate**: 250 baud (4ms per bit)
- **Mark Frequency**: 1200 Hz (binary 1)
- **Space Frequency**: 2200 Hz (binary 0)
- **Sample Rate**: 44.1 kHz
- **Amplitude**: 25% of maximum (optimized for radio VOX triggering)

**Demodulation**:
- **Method**: Zero-crossing detection (counts time between sign changes)
- **UART State Machine**: Idle → StartBit → 8 DataBits (LSB first) → StopBit
- **Squelch Threshold**: 0.01 RMS (1% of max signal)
- **Input Gain**: Configurable multiplier (default 1.0x)

**Known Limitations**:
- Fixed zero-crossing threshold may not work equally well on all hardware
- No adaptive gain control (AGC)
- No Forward Error Correction (FEC)
- Sensitive to background noise

#### `CustomProtocol.cs` - Packet Framing
**Packet Structure**:
```
[Sync Word: 0xAA 0x55] [Length: 1 byte] [Type: 1 byte] [Payload: encrypted] [Checksum: 1 byte]
```

**Encryption**:
- **Method**: XOR cipher with user-provided key (default: "RADIO")
- **Security**: **NOT cryptographically secure** - basic obfuscation only
- **Key Length**: 1-64 ASCII characters

**Packet Types**:
- `0x01` - Text message
- `0x02` - File header (filename + size)
- `0x03` - File chunk (sequence ID + 200 bytes data)

**Checksum**: Simple sum of Type + Encrypted Payload bytes

#### `AudioService.cs` - NAudio Interface
**Transmission**:
- Uses `BufferedWaveProvider` for gap-free multi-packet queueing
- 10-minute buffer capacity for large file transfers
- Background thread monitors buffer and fires completion event

**Reception**:
- Continuous audio capture via `WaveInEvent`
- Mono, 44.1 kHz
- Passes raw audio bytes to demodulator

**Loopback Mode**:
- Software-only mode for testing (no actual audio hardware)
- Audio bytes immediately routed from modulator to demodulator

#### `FileTransferService.cs` - File Chunking & Reassembly
**Chunking**:
- Maximum chunk size: **200 bytes** (payload only, not including protocol overhead)
- Sequence IDs allow out-of-order reception and duplicate detection
- First packet is always file header with metadata

**Reassembly**:
- Fixed-size buffer allocated based on file size from header
- Uses HashSet to track received unique chunk IDs
- No retransmission mechanism (fire-and-forget protocol)

**Timeout Detection**:
- **Silence timeout**: 10 seconds with no packets received
- **Max time timeout**: Calculated as `chunks * 2.5 seconds + 10 seconds buffer`
- Timer checks every 2 seconds

**Image Compression**:
- `.cimg` files are automatically decompressed on reception
- 12-bit color format (4 bits per channel, 2 pixels per 3 bytes)
- ~50% size reduction for images

#### `ImageCompressionService.cs` - 12-bit Color Compression
**Algorithm**:
```
RGB888 (24-bit) → RGB444 (12-bit)
Input:  [R8][G8][B8] [R8][G8][B8] (6 bytes for 2 pixels)
Output: [R4G4][B4R4][G4B4]       (3 bytes for 2 pixels)
```

**Quality Loss**: Each color channel reduced from 256 values to 16 values  
**Use Case**: Faster transmission of images where quality loss is acceptable

#### `MainViewModel.cs` - MVVM Coordinator
**Key Responsibilities**:
- Device selection and audio routing
- Real-time input/output frequency and volume meters
- Encryption key management and persistence
- File transfer progress tracking
- Transmission state management (prevents simultaneous send/receive)

**Device Selection Logic**:
```
Index 0: Loopback (software mode)
Index 1+: Real hardware devices (index - 1 = actual device number)
```

---

## 🚀 Getting Started

### Requirements
- **Windows 10/11** (WPF application)
- **.NET 8.0 Runtime** (or SDK for development)
- **Audio Hardware**:
  - Built-in mic/speakers (for testing)
  - Virtual Audio Cable (recommended for loopback testing)
  - APRS-K2 or similar cable (for radio connection)

### Basic Operation

1. **Select Audio Devices**:
   - Choose **Input Device** (microphone/radio input)
   - Choose **Output Device** (speakers/radio output)
   - For software testing, select "Loopback" for both

2. **Set Encryption Key**:
   - Both sender and receiver **must use the same key**
   - Default: "RADIO"
   - Key is saved automatically in `%LocalAppData%\RadioData\RadioData.settings.json`

3. **Advanced Tuning (NEW - Fixes Asymmetry)**:
   - **Input Gain** (0.5x - 2.0x): Amplify weak signals, default 1.0x
     - Increase if signal too weak to decode
     - Decrease if seeing clipping warnings
   - **Zero-Crossing Threshold** (10-20): Tune for your hardware, default 14
     - Lower value = more sensitive to Space (2200 Hz)
     - Higher value = more sensitive to Mark (1200 Hz)
     - Adjust if one direction works but not the other
   - **Start Bit Compensation** (-5.0 to +5.0): Adjust timing, default -2.0
     - Negative = more delay (slower detection)
     - Positive = less delay (faster detection)
     - Adjust if getting bit errors or garbled data

4. **Transmit Text**:
   - Type message (max 120 characters)
   - Click **Send text**
   - Monitor output meters for frequency (should show 1200-2200 Hz)

5. **Transmit File**:
   - Click **Send file** and select file
   - Check **Compress images** for .jpg/.png files (optional)
   - App shows progress and estimated time
   - Large files: confirm transmission time estimate

6. **Receive**:
   - App automatically listens on selected input device
   - Received text appears in System Log
   - Received files saved to `ReceivedFiles` folder
   - Click **Open Received Files** to view folder

---

## 🛠️ Troubleshooting

### Symptom: One Direction Works, Other Doesn't

**Root Cause**: This is the main known issue - asymmetric demodulation reliability.

**✅ FIXED**: You can now tune parameters to fix this!

**Solution Steps**:

1. **Start with defaults**:
   - Zero-Crossing Threshold: 14
   - Start Bit Compensation: -2.0
   - Input Gain: 1.0x

2. **If receiving fails in one direction**:
   - Open "Advanced Tuning" section in UI
   - **Try Input Gain = 1.5x** (amplifies weak signals)
     - Watch System Log for clipping warnings
     - If clipping occurs, reduce to 1.2x
   - **Try Zero-Crossing Threshold = 12** (more sensitive to higher frequencies)
   - **Try Start Bit Compensation = -3.0** (adds more detection delay)

3. **If getting garbled/corrupted data**:
   - **Try Input Gain = 0.8x** (reduces overly strong signal)
   - **Try Zero-Crossing Threshold = 16** (less sensitive to noise)
   - **Try Start Bit Compensation = -1.0** (less delay)

4. **Systematic approach** (most reliable):
   ```
   For each Zero-Crossing value from 10 to 20:
     - Send 10 test messages
     - Count successes
   
   Use the threshold with best success rate
   
   Then test Start Bit Compensation from -5 to +5
   ```

5. **Monitor indicators**:
   - System Log shows `[WARNING] Input gain too high` if clipping
   - RMS levels should be 0.01 to 0.5 (not near 1.0)
   - Settings logged when changed: `[Settings] Zero-crossing threshold: 12`

**Old Workarounds** (still valid):
- **Swap radio positions** (transmitter becomes receiver)
- **Use same audio interface model** on both sides if possible
- **Test with virtual audio cable** to isolate hardware vs software issues
