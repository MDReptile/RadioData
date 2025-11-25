# Quick Fix Guide - RadioData Asymmetry

## ?? Your Specific Issue

**Client 2 Output:**
```
[RMS: 0.1276] [00][00] [RMS: 0.3408] [FC] [RMS: 0.3590]
```

**Problem:** Decoder is trying to process ambient noise!

---

## ? Quick Fix (30 seconds)

### Client 2 Settings

1. Open **RadioData**
2. Find **"ADVANCED TUNING"** section (right side of window)
3. Adjust **"Squelch Threshold"** slider:

```
Squelch Threshold (0.000 - 0.100)
[???????????????????] 0.050    ? Move slider here!
```

4. Click anywhere to apply
5. Look for log message:
```
[Settings] Squelch threshold: 0.050
```

6. **Test:** Should no longer see random bytes during silence!

---

## ?? How to Find the Right Value

### Method 1: Quick Guess (Your Case)
```
Your noise RMS: 0.127 to 0.423
Recommended: 0.050 (blocks noise up to 5%)
If still noisy: 0.080 or 0.100
```

### Method 2: Systematic
```
1. Don't transmit anything
2. Watch System Log for highest [RMS: X.XXXX]
3. Set squelch to (highest × 1.5)
4. Example: 
   Highest noise = 0.040
   Set squelch = 0.060
```

---

## ? Success Indicators

### Before Fix
```
? [RMS: 0.1276] [00][00]        ? Random noise
? [RMS: 0.3408] [FC]            ? More noise
? [RMS: 0.3590] [00][00][00]    ? Still noise
```

### After Fix
```
? (No RMS logging during silence)
? TX: Hello World
? [RMS: 0.0135] [AA][55] ...    ? Only real signal
? RX: Hello World
```

---

## ?? All Tuning Parameters (for reference)

If squelch alone doesn't fix it, try these:

```
????????????????????????????????????????????????????????
? Parameter                    ? Default ? Try This    ?
????????????????????????????????????????????????????????
? Squelch Threshold            ? 0.010   ? 0.050-0.080 ? ? FIX THIS FIRST!
? Input Gain                   ? 1.0x    ? 1.0-1.5x    ?
? Zero-Crossing Threshold      ? 14      ? 12-16       ?
? Start Bit Compensation       ? -2.0    ? -3.0 to -1.0?
????????????????????????????????????????????????????????
```

---

## ?? Troubleshooting

### Not receiving anything now?
? Squelch too high, lower to 0.030

### Still seeing garbage bytes?
? Squelch too low, increase to 0.080 or 0.100

### Works sometimes, fails other times?
? Intermittent noise, set squelch conservatively higher

---

## ?? What Squelch Does

**Simple Explanation:**
```
Think of squelch as a "volume gate"

Squelch = 0.050 means:
  "Ignore any audio below 5% volume"
  
Your ambient noise = 12-42% (WAY too high!)
Your actual signal = should be >50%

So squelch at 5% will:
  ? Block noise (1-42%)
  ? Allow signal (>50%)
```

---

## ?? Quick Test Procedure

```
[1] Set squelch to 0.050 on Client 2
    ??? Check log: [Settings] Squelch threshold: 0.050

[2] Don't transmit, just listen
    ??? Should NOT see: [00], [FC], random bytes
    ??? Should NOT see: [RMS: X.XXXX] constantly

[3] Client 1 transmits "Hello World"
    ??? Should see: [RMS: X.XXXX] when signal arrives
    ??? Should see: [AA][55] sync word
    ??? Should see: RX: Hello World

[4] Client 2 transmits "Hello World"
    ??? Should see: TX: Hello World
    ??? Client 1 should receive it

[5] Success! Both directions work! ??
```

---

## ?? Settings Are Saved

Once you set the squelch, it's **automatically saved** to:
```
%LocalAppData%\RadioData\RadioData.settings.json
```

You only need to configure it **once per computer**!

---

## ?? Expected Results

### Client 1 (Already Working)
```
No changes needed
Keep squelch at 0.010
Continue working as before
```

### Client 2 (The Problem Client)
```
Change squelch from 0.010 ? 0.050
Noise filtering activated
Should now work like Client 1!
```

### Result
```
Client 1 ? Client 2: ? Works
Client 2 ? Client 1: ? Works
Asymmetry: ? FIXED!
```

---

## ?? If You Still Have Issues

Try this diagnostic sequence:

```python
# 1. Measure ambient noise
print("Don't transmit, just watch log...")
max_rms = 0.150  # Example: your highest RMS

# 2. Set squelch
squelch = max_rms * 1.5  # Add 50% margin
if squelch > 0.100:
    squelch = 0.100  # Slider max

# 3. Test
print(f"Set squelch to: {squelch:.3f}")

# 4. If signal not received:
input_gain = 1.5  # Boost weak signals
print(f"Set Input Gain to: {input_gain}x")

# 5. Should work now!
```

---

## ?? Bottom Line

**One setting to change on Client 2:**
```
Squelch Threshold: 0.010 ? 0.050
```

**Expected time to fix:**
```
30 seconds to change setting
1 minute to test
DONE! ?
```

**Why this works:**
```
Your problem = Noise (12-42% RMS)
Solution = Filter it out (squelch = 5%)
Result = Clean decode!
```

---

**TL;DR:**  
Move the **Squelch Threshold** slider to **0.050** on Client 2. Done! ??
