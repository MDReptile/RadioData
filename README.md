# RadioData - AFSK Audio Data Transfer

> Built with Google Antigravity and Visual Studio, using Gemini 3 and Claude Sonnet 4.5  
> Minimal manual coding - mostly vibe coding and AI-assisted development

**Transfer text messages and files over audio using AFSK modulation - perfect for ham radio, walkie-talkies, or any audio link.**

![RadioData Application Screenshot](screenshot.png)

### Example Hardware Configuration

![Hardware Setup - Surface tablet with Baofeng UV-5R and BTECH APRS-K2 cable](hardware-setup.png)

*Microsoft Surface tablet connected to Baofeng UV-5R radio via BTECH APRS-K2 cable*


---

## Disclaimer
- This is provided for educational purposes, and any use of this software is at your own risk.
- Check local laws and regulations regarding radio transmission on whichever frequency / channel you are using.

## Features

- **Text Messaging**: Send up to 120 characters of encrypted text over audio
- **Client Names**: 10-character identifiers auto-generated on first launch, shown in chat messages
- **File Transfer**: Split large files into 200-byte packets and reassemble on reception
- **Image Compression**: Optional 12-bit color compression (~50% size reduction) for faster image transfers
- **SHA256-Based Encryption**: Secure encryption using configurable shared keys (1-64 characters) with SHA256 key derivation
- **Real-time Audio Meters**: Monitor input/output frequency and volume levels
- **Loopback Testing**: Software-only mode for testing without physical audio hardware
- **Advanced Tuning**: Adjustable parameters to optimize for different audio hardware and radio configurations
- **Settings Persistence**: Automatically saves device selections, encryption keys, client name, and all tuning parameters
- **Progress Tracking**: Visual progress bars and status updates for file transfers
- **Received Files Management**: One-click access to received files folder
- **File Overwrite Handling**: Prompts for overwrite or auto-numbering of duplicate files

## How It Works

RadioData uses **Audio Frequency Shift Keying (AFSK)** to encode digital data as audio tones:

- **Mark (binary 1)**: 1200 Hz tone
- **Space (binary 0)**: 2200 Hz tone
- **Baud Rate**: 250 baud (4 milliseconds per bit)
- **Sample Rate**: 44.1 kHz
- **Modulation**: Same standard used by APRS and packet radio

### Data Flow

1. **Encoding**: Text/files ? encrypted packets ? UART framing ? AFSK modulation ? audio samples
2. **Transmission**: Audio samples ? sound card/radio ? air/cable ? receiving sound card/radio
3. **Decoding**: Audio samples ? zero-crossing detection ? UART decoding ? packet reassembly ? file/text display

### Packet Structure

```
[Sync: 0xAA 0x55] [Length: 1 byte] [Type: 1 byte] [Encrypted Payload] [Checksum: 1 byte]
```

**Packet Types**:
- `0x01` - Text message
- `0x02` - File header (filename + size)
- `0x03` - File chunk (sequence ID + data)

**Encryption**: XOR cipher with SHA256-derived key (default password: "RADIO")
- Password is hashed using SHA256 to create a 256-bit encryption key
- Even a single character difference in the password produces a completely different key
- Provides avalanche effect - changing one character makes entire message unreadable
- Key is cached for performance to avoid re-hashing on every packet

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

1. **Set Your Name**:
   - Enter your name (max 10 characters) in the top-right text box
   - Auto-generated unique name on first launch, but you can change it anytime
   - Shown as `[YourName]` in chat messages

2. **Select Audio Devices**:
   - **Input Device**: Microphone or radio input
   - **Output Device**: Speakers or radio output
   - For software testing, select "Loopback" for both

3. **Set Encryption Key**:
   - Both sender and receiver must use the **same key**
   - Default: "RADIO"
   - Automatically saved in `%LocalAppData%\RadioData\RadioData.settings.json`

4. **Send Text**:
   - Type message (max 120 characters)
   - Click **Send Text**
   - Monitor output meters (should show 1200-2200 Hz alternating)

5. **Send File**:
   - Click **Send File** and select a file
   - Optionally enable **Compress Images** for .jpg/.png files
   - Confirm transmission time estimate for large files
   - Files are saved to `ReceivedFiles` folder automatically

**----- NOTE:** If you are trying to use a radio and APRS cable, you want to enable VOX
     which should allow your voice to trigger transmission, which allows the PC to transmit.
     You want squelch setting on the radio to be low enough to pick up pretty easily, and
     you should use a channel / frequency that isn't noisy or in use, otherwise transmission
     may fail because of interference from other audio over the channel / frequency.

### Advanced Tuning

Open the **Advanced Tuning** section to adjust parameters for your specific hardware:

- **Squelch Threshold** (0.000 - 0.100): Minimum signal strength to decode (default: 0.01)
  - Increase to 0.02-0.05 if decoder processes background noise
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

All settings are automatically saved and restored on next launch.

## Troubleshooting

### One Direction Works, Other Doesn't

This is typically caused by differences in audio hardware characteristics between sender and receiver.

**Solution Steps**:

1. **Check for ambient noise**:
   - Look at System Log for random bytes or high RMS values during silence
   - Try **Squelch Threshold = 0.02-0.05** to filter noise

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
   - First check: Are you receiving random bytes during silence? ? Increase squelch to 0.02-0.05
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
| Random bytes during silence | Increase Squelch Threshold to 0.02-0.05 or higher |
| Clipping warnings | Reduce Input Gain below 1.0x |
| VOX not triggering | Increase radio output volume or output amplitude |
| Settings not saving | Check write permissions for `%LocalAppData%\RadioData` folder |

## Technical Details

### Modulation Parameters

- **Baud Rate**: 250 baud (4ms per bit)
- **Mark Frequency**: 1200 Hz (binary 1)
- **Space Frequency**: 2200 Hz (binary 0)
- **Sample Rate**: 44.1 kHz
- **Amplitude**: 25% of maximum (optimized for radio VOX)
- **Preamble**: 1200ms of alternating tones before first packet in file transfer

### Protocol Specifications

- **Maximum Packet Size**: 255 bytes (including overhead)
- **Maximum Payload**: ~240 bytes per packet
- **File Chunk Size**: 200 bytes
- **Text Message Size**: 120 characters
- **Transmission Speed**: ~250 bits/second (~31 bytes/second)
- **Estimated Time**: ~9.5 seconds per file packet (including overhead)
- **First Packet Time**: ~13.5 seconds (includes 1200ms preamble)

### File Transfer

Files are split into 200-byte chunks and transmitted sequentially. Each chunk includes a sequence ID for reassembly. The receiver tracks received chunks and reports progress. Timeout detection alerts if transfer stalls.

**Timeout Settings**:
- Silence timeout: 20 seconds without any packets (allows for normal 9.5s inter-packet delay)
- Total transfer timeout: Calculated as `13.5s (header) + (chunks × 9.5s) + 15s buffer`
- Transfer is considered failed only if no packets arrive for 20 seconds or total time exceeds the calculated maximum

### Settings Storage

All settings are saved to `%LocalAppData%\RadioData\RadioData.settings.json` including:
- Client name
- Encryption key
- Selected input/output devices
- Input gain
- Zero-crossing threshold
- Start bit compensation
- Squelch threshold
- Image compression preference

Settings are automatically loaded on application startup.

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
