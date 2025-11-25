# Known Issues

## UI Issues

### Send Buttons Not Fully Disabling During Short Transmissions

**Status:** Known Issue  
**Severity:** Low  
**Affects:** Text message transmission

**Description:**
The Send Text and Send File buttons are properly disabled during file transfers and reception, but may appear enabled for a brief moment during short text message transmissions.

**Root Cause:**
- `IsTransmitting` flag is set correctly
- Text messages transmit very quickly (< 1 second)
- `TransmissionCompleted` event fires almost immediately
- UI update may not reflect the disabled state in time

**Workaround:**
- None needed - functionally works correctly (CanTransmit prevents execution)
- Visual state just updates very fast

**Proposed Fix:**
Add minimum transmission duration or separate handling for text vs file transmissions.

---

## Versioning

### Current Scheme
- Format: `0.XX` (e.g., 0.08 in csproj, displays as `0.8` in UI)
- Display uses `.ToString(2)` which drops trailing zeros (0.08 ? 0.8, 0.10 ? 0.1)
- Auto-increments on push: 0.01 ? 0.02 ? ... ? 0.10 ? ... ? 0.99 ? 1.0
- Rolls over to 1.0 at version 100

### Version Display Examples
- `0.08` (csproj) ? `v0.8` (UI)
- `0.10` (csproj) ? `v0.1` (UI) 
- `0.15` (csproj) ? `v0.15` (UI)
- `1.0` (csproj) ? `v1.0` (UI)

This is standard behavior - trailing zeros after decimal are typically omitted in version displays.

### Considerations for Future
Current scheme allows 100 versions before 1.0. Consider switching to semantic versioning (`MAJOR.MINOR.PATCH`) for more flexibility:
- `0.7.0` ? `0.7.1` (bugfix) ? `0.8.0` (feature) ? `1.0.0` (release)
- Allows bug fixes without consuming version numbers
- Industry standard format

---

## Performance Notes

### Audio Transmission Timing
- 250 baud rate chosen for maximum reliability
- Short text messages: ~2-4 seconds
- File packets: ~9.5 seconds each (after preamble)
- Preamble: 1200ms (VOX activation delay)

### Squelch Behavior
- Default threshold: 0.01 (1% RMS)
- Some environments require higher thresholds (0.05-0.06)
- Receiver "busy" state clears after 2 seconds of silence
- Prevents transmission collisions

---

Last Updated: 2024-11-25
