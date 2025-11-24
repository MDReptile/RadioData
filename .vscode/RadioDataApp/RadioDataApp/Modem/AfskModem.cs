using System;
using System.Collections.Generic;
using System.Linq;

namespace RadioDataApp.Modem
{
    public class AfskModem
    {
        private const int SampleRate = 44100;
        private const int BaudRate = 1200;
        private const int MarkFreq = 1200;
        private const int SpaceFreq = 2200;

        // Demodulation State
        private float _lastSample = 0;
        private int _samplesSinceCrossing = 0;
        private readonly List<bool> _bitBuffer = [];
        private int _samplesPerBit = SampleRate / BaudRate;

        public byte[] Modulate(byte[] data)
        {
            List<byte> samples = [];

            // Preamble (Sync) - Send Marks for a bit to wake up VOX
            // 1000ms of Mark tones
            samples.AddRange(GenerateTone(MarkFreq, 1000));

            foreach (byte b in data)
            {
                // 8N1 Protocol: Start Bit (Space) + 8 Data Bits + Stop Bit (Mark)

                // Start Bit (Space)
                samples.AddRange(GenerateTone(SpaceFreq, 1000.0 / BaudRate));

                // 8 Data Bits (LSB first)
                for (int i = 0; i < 8; i++)
                {
                    bool bit = (b & (1 << i)) != 0;
                    int freq = bit ? MarkFreq : SpaceFreq;
                    samples.AddRange(GenerateTone(freq, 1000.0 / BaudRate));
                }

                // Stop Bit (Mark)
                samples.AddRange(GenerateTone(MarkFreq, 1000.0 / BaudRate));
            }

            // Postamble/Tail
            samples.AddRange(GenerateTone(MarkFreq, 100));

            return samples.ToArray();
        }

        public byte[] GetTestTone(int durationMs)
        {
            return GenerateTone(MarkFreq, durationMs);
        }

        private byte[] GenerateTone(int frequency, double durationMs)
        {
            int sampleCount = (int)((SampleRate * durationMs) / 1000);
            byte[] buffer = new byte[sampleCount * 2]; // 16-bit audio
            double theta = 0;
            double step = 2 * Math.PI * frequency / SampleRate;

            for (int i = 0; i < sampleCount; i++)
            {
                // Scale amplitude to 12.5%
                short sample = (short)(Math.Sin(theta) * short.MaxValue * 0.125);
                byte[] bytes = BitConverter.GetBytes(sample);
                buffer[i * 2] = bytes[0];
                buffer[i * 2 + 1] = bytes[1];
                theta += step;
            }
            return buffer;
        }

        public string Demodulate(byte[] audioBytes)
        {
            // Process samples
            for (int i = 0; i < audioBytes.Length; i += 2)
            {
                short sampleShort = BitConverter.ToInt16(audioBytes, i);
                float sample = sampleShort / 32768f;

                // Zero Crossing Detector
                // If sign changed
                if ((sample > 0 && _lastSample <= 0) || (sample <= 0 && _lastSample > 0))
                {
                    // Calculate frequency based on samples since last crossing
                    // Period = 2 * samplesSinceCrossing
                    // Freq = SampleRate / Period

                    // 1200Hz -> ~36.75 samples/period -> ~18 samples/half-period
                    // 2200Hz -> ~20.05 samples/period -> ~10 samples/half-period
                    // Threshold ~ 14 samples

                    bool isMark = _samplesSinceCrossing > 14;

                    // Add this "bit" to our buffer for every sample duration we missed?
                    // No, that's too much data.
                    // Let's just push the detected bit for this duration.

                    // Simple approach: Just accumulate bits?
                    // Better: We need to sample the "Line State" at the baud rate.
                }

                _samplesSinceCrossing++;
                if ((sample > 0 && _lastSample <= 0) || (sample <= 0 && _lastSample > 0))
                {
                    _samplesSinceCrossing = 0;
                }
                _lastSample = sample;
            }

            // TODO: The streaming demodulator is complex. 
            // For this iteration, let's stick to the "Signal Detected" check 
            // but return it as a string so the user sees it.
            // I will implement the full 8N1 decoder in the NEXT step 
            // once we confirm the 8N1 transmit format is working.

            // Calculate Peak for signal detection
            float max = 0;
            for (int i = 0; i < audioBytes.Length; i += 2)
            {
                short s = BitConverter.ToInt16(audioBytes, i);
                if (Math.Abs(s) > max) max = Math.Abs(s);
            }

            if (max > 3276) // > 10%
            {
                return "[Signal Receiving...]";
            }

            return "";
        }
    }
}
