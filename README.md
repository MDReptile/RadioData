# RadioData - AFSK Audio Data Transfer

> Built with Google Antigravity and Visual Studio, using Gemini 3 and Claude Sonnet 4.5  
> Minimal manual coding - mostly vibe coding and AI-assisted development

**Transfer text messages and files over audio using AFSK modulation - perfect for ham radio, walkie-talkies, or any audio link.**

![RadioData Application Screenshot](screenshot.png)

### Example Hardware Configuration

![Hardware Setup - Surface tablet with Baofeng UV-5R and BTECH APRS-K2 cable](hardware-setup.png)

*Microsoft Surface tablet connected to Baofeng UV-5R radio via BTECH APRS-K2 cable*


---

## Features

- **Text Messaging**: Send up to 120 characters of encrypted text over audio
- **File Transfer**: Split large files into 200-byte packets and reassemble on reception
- **Image Compression**: Optional 12-bit color compression (~50% size reduction) for faster image transfers
- **XOR Encryption**: Basic encryption using configurable shared keys (1-64 characters)
- **Real-time Audio Meters**: Monitor input/output frequency and volume levels
- **Loopback Testing**: Software-only mode for testing without physical audio hardware
- **Advanced Tuning**: Adjustable parameters to optimize for different audio hardware and radio configurations
- **Settings Persistence**: Automatically saves device selections and encryption keys
- **Progress Tracking**: Visual progress bars and status updates for file transfers
- **Received Files Management**: One-click access to received files folder

## How It Works

RadioData uses **Audio Frequency Shift Keying (AFSK)** to encode digital data as audio tones:

- **Mark (binary 1)**: 1200 Hz tone
- **Space (binary 0)**: 2200 Hz tone
- **Baud Rate**: 250 baud (4 milliseconds per bit)
- **Sample Rate**: 44.1 kHz
- **Modulation**: Same standard used by APRS and packet radio

### Data Flow

1. **Encoding**: Text/files → encrypted packets → UART framing → AFSK modulation → audio samples
2. **Transmission**: Audio samples → sound card/radio → air/cable → receiving sound card/radio
3. **Decoding**: Audio samples → zero-crossing detection → UART decoding → packet reassembly → file/text display

### Packet Structure

```
[Sync: 0xAA 0x55] [Length: 1 byte] [Type: 1 byte] [Encrypted Payload] [Checksum: 1 byte]
```

**Packet Types**:
- `0x01` - Text message
- `0x02` - File header (filename + size)
- `0x03` - File chunk (sequence ID + data)

**Encryption**: XOR cipher with user key (default: "RADIO")  
**Checksum**: Simple sum validation

## Getting Started

### Requirements

- **Windows 10/11** (WPF application)
- **.NET 8.0 Runtime** ([Download here](https://dotnet.microsoft.com/download/dotnet/8.0))
- **Audio Hardware**:
  - Built-in mic/speakers (for testing)
  - APRS cable or audio interface (for radio connection)

### Installation

1. Download the latest release or clone this repository
2. Ensure .NET 8.0 Runtime is installed
3. Run `RadioDataApp.exe`

### Basic Usage

1. **Select Audio Devices**:
   - **Input Device**: Microphone or radio input
   - **Output Device**: Speakers or radio output
   - For software testing, select "Loopback" for both

2. **Set Encryption Key**:
   - Both sender and receiver must use the **same key**
   - Default: "RADIO"
   - Automatically saved in `%LocalAppData%\RadioData\settings.json`

3. **Send Text**:
   - Type message (max 120 characters)
   - Click **Send Text**
   - Monitor output meters (should show 1200-2200 Hz alternating)

4. **Send File**:
   - Click **Send File** and select a file
   - Optionally enable **Compress Images** for .jpg/.png files
   - Confirm transmission time estimate for large files
   - Files are saved to `ReceivedFiles` folder automatically

### Advanced Tuning

Open the **Advanced Tuning** section to adjust parameters for your specific hardware:

- **Squelch Threshold** (0.000 - 0.100): Minimum signal strength to decode (default: 0.01)
  - Increase to 0.05+ if decoder processes background noise
  - Should be below signal strength but above ambient noise

- **Input Gain** (0.5x - 2.0x): Amplify weak signals (default: 1.0x)
  - Increase to 1.5x for weak signals
  - Decrease to 0.8x if seeing clipping warnings

- **Zero-Crossing Threshold** (10-20): Distinguishes 1200 Hz from 2200 Hz (default: 14)
  - Lower = more sensitive to Space (2200 Hz)
  - Higher = more sensitive to Mark (1200 Hz)
  - Adjust if one direction works but not the other

- **Start Bit Compensation** (-5.0 to +5.0): Timing offset (default: -2.0)
  - Negative = more delay (slower detection)
  - Positive = less delay (faster detection)
  - Adjust if getting garbled data

## Troubleshooting

### One Direction Works, Other Doesn't

This is typically caused by differences in audio hardware characteristics between sender and receiver.

**Solution Steps**:

1. **Check for ambient noise**:
   - Look at System Log for random bytes or high RMS values during silence
   - Try **Squelch Threshold = 0.05** to filter noise

2. **Start with defaults**:
   - Zero-Crossing: 14
   - Start Bit Compensation: -2.0
   - Input Gain: 1.0x
   - Squelch: 0.01

3. **If receiving fails**:
   - Try **Input Gain = 1.5x** (watch for clipping warnings)
   - Try **Zero-Crossing = 12** (more sensitive to high frequencies)
   - Try **Start Bit Compensation = -3.0**

4. **If getting garbled data**:
   - First check: Are you receiving random bytes during silence? → Increase squelch to 0.05-0.10
   - Try **Input Gain = 0.8x**
   - Try **Zero-Crossing = 16**
   - Try **Start Bit Compensation = -1.0**

### Radio and Sound Card Compatibility

**Important**: Even radios of the same model (e.g., Baofeng UV-5R) may require different settings:

- **Volume Level**: Some radios output hotter audio than others - adjust radio volume or Input Gain
- **Input Gain**: Different sound cards have different sensitivity - adjust Input Gain to compensate
- **Machine-to-Machine Variation**: The same radio may behave differently on two computers with different sound cards
- **VOX Sensitivity**: Some radios need higher output volume to trigger VOX reliably

**Tips**:
- Test with loopback mode first to verify software is working
- Use same brand/model of sound cards on both sides if possible
- Start with radio volume at 50% and adjust Input Gain instead
- Monitor RMS levels in System Log - aim for 0.01 to 0.5 (not near 1.0)

### Common Issues

| Problem | Solution |
|---------|----------|
| No audio output | Check output device selection, verify not in loopback mode |
| No audio input detected | Check input device selection, verify microphone permissions |
| Checksum failures | Increase Input Gain or adjust Zero-Crossing Threshold |
| Random bytes during silence | Increase Squelch Threshold to 0.05 or higher |
| Clipping warnings | Reduce Input Gain below 1.0x |
| VOX not triggering | Increase radio output volume or output amplitude |

## Technical Details

### Modulation Parameters

- **Baud Rate**: 250 baud (4ms per bit)
- **Mark Frequency**: 1200 Hz (binary 1)
- **Space Frequency**: 2200 Hz (binary 0)
- **Sample Rate**: 44.1 kHz
- **Amplitude**: 25% of maximum (optimized for radio VOX)
- **Preamble**: 1200ms of alternating tones before each transmission

### Protocol Specifications

- **Maximum Packet Size**: 255 bytes (including overhead)
- **Maximum Payload**: ~240 bytes per packet
- **File Chunk Size**: 200 bytes
- **Transmission Speed**: ~250 bits/second (~31 bytes/second)
- **Estimated Time**: ~9.5 seconds per file packet (including overhead)

### File Transfer

Files are split into 200-byte chunks and transmitted sequentially. Each chunk includes a sequence ID for reassembly. The receiver tracks received chunks and reports progress. Timeout detection alerts if transfer stalls.

**Timeout Settings**:
- Silence timeout: 10 seconds without packets
- Max time: Calculated as `chunks × 9.5s + 10s buffer`

### Architecture

- **MVVM Pattern**: Clean separation of UI and business logic
- **NAudio**: Audio I/O and buffering
- **CommunityToolkit.MVVM**: Modern MVVM helpers
- **MaterialDesignThemes**: Modern UI components

## Building from Source

### Prerequisites

- Visual Studio 2022 or later
- .NET 8.0 SDK
- Windows 10/11

### Dependencies

- NAudio 2.2.1
- CommunityToolkit.Mvvm 8.4.0
- MaterialDesignThemes 5.3.0

### Build Steps

```bash
git clone https://github.com/MDReptile/RadioData.git
cd RadioData
dotnet build RadioDataApp/RadioDataApp.csproj
dotnet run --project RadioDataApp/RadioDataApp.csproj
```

## License

This project is open source. Feel free to use, modify, and distribute.

## Acknowledgments

Built using Visual Studio with Copilot and Google Antigravity AI-assisted development with Gemini 3 and Claude Sonnet 4.5. The majority of code was generated through natural language prompts and vibe coding, with minimal manual intervention.
