using System;
using System.Collections.Generic;
using System.Linq;

namespace RadioDataApp.Modem
{
    public class AfskModem
    {
        private const int SampleRate = 44100;
        private const int BaudRate = 250; // Reduced to 250 for maximum reliability
        private const int MarkFreq = 1200;
        private const int SpaceFreq = 2200;
        private const double SamplesPerBit = (double)SampleRate / BaudRate;
        private const float SquelchThreshold = 0.01f; // Minimum RMS level to attempt decoding (1% of max)

        // Demodulation State
        private float _lastSample = 0;
        private int _samplesSinceCrossing = 0;
        private bool _currentLevel = true; // true = Mark (1200), false = Space (2200)

        // UART State Machine
        private enum UartState { Idle, StartBit, DataBits, StopBit }
        private UartState _state = UartState.Idle;
        private double _samplesInCurrentState = 0;
        private int _bitIndex = 0;
        private int _currentByte = 0;

        // Modulation State
        private double _phase = 0;

        // Input gain for weak signals
        public float InputGain { get; set; } = 1.0f; // Default 1.0 = no amplification, 2.0 = double, etc.

        // Configurable zero-crossing threshold
        public int ZeroCrossingThreshold { get; set; } = 14; // Samples

        // Compensate for detection latency in Start Bit
        public double StartBitCompensation { get; set; } = -2.0; // Samples

        // Event for raw byte debugging
        public event EventHandler<byte>? RawByteReceived;

        // Event for RMS level logging
        public event EventHandler<float>? RmsLevelDetected;

        private List<byte> _byteBuffer = [];

        public byte[] Modulate(byte[] data, bool includePreamble = true, int preambleDurationMs = 1200)
        {
            List<byte> samples = [];
            _phase = 0; // Reset phase for new transmission

            // Preamble (Sync) - wake up VOX before data
            if (includePreamble)
            {
                AddTone(samples, MarkFreq, preambleDurationMs);
            }

            foreach (byte b in data)
            {
                // Start Bit (Space)
                AddTone(samples, SpaceFreq, 1000.0 / BaudRate);

                // 8 Data Bits (LSB first)
                for (int i = 0; i < 8; i++)
                {
                    bool bit = (b & (1 << i)) != 0;
                    int freq = bit ? MarkFreq : SpaceFreq;
                    AddTone(samples, freq, 1000.0 / BaudRate);
                }

                // Stop Bit (Mark)
                AddTone(samples, MarkFreq, 1000.0 / BaudRate);
            }

            // Postamble/Tail - keep VOX open until receiver processes data
            AddTone(samples, MarkFreq, 800);

            return samples.ToArray();
        }

        public byte[] GetTestTone(int durationMs)
        {
            List<byte> samples = [];
            _phase = 0;
            AddTone(samples, MarkFreq, durationMs);
            return samples.ToArray();
        }

        private void AddTone(List<byte> buffer, int frequency, double durationMs)
        {
            int sampleCount = (int)((SampleRate * durationMs) / 1000);
            double step = 2 * Math.PI * frequency / SampleRate;

            for (int i = 0; i < sampleCount; i++)
            {
                // Scale amplitude to 25% for radio VOX triggering
                short sample = (short)(Math.Sin(_phase) * short.MaxValue * 0.25);
                byte[] bytes = BitConverter.GetBytes(sample);
                buffer.Add(bytes[0]);
                buffer.Add(bytes[1]);
                _phase += step;
                if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
            }
        }

        public CustomProtocol.DecodedPacket? Demodulate(byte[] audioBytes)
        {
            // Calculate RMS volume to implement squelch
            float sumSquares = 0;
            int sampleCount = audioBytes.Length / 2;

            for (int i = 0; i < audioBytes.Length; i += 2)
            {
                short sampleShort = BitConverter.ToInt16(audioBytes, i);
                float sample = sampleShort / 32768f;
                sumSquares += sample * sample;
            }

            float rms = (float)Math.Sqrt(sumSquares / sampleCount);

            // Emit RMS level for diagnostic logging
            RmsLevelDetected?.Invoke(this, rms);

            // If volume is below squelch threshold, don't attempt to decode
            if (rms < SquelchThreshold)
            {
                return null;
            }

            List<byte> newBytes = [];
            bool _clippingDetected = false;

            for (int i = 0; i < audioBytes.Length; i += 2)
            {
                short sampleShort = BitConverter.ToInt16(audioBytes, i);
                float sample = sampleShort / 32768f;

                // Apply input gain to amplify weak signals
                sample *= InputGain;

                // Detect and handle clipping
                if (Math.Abs(sample) > 1.0f)
                {
                    _clippingDetected = true;
                    sample = Math.Clamp(sample, -1.0f, 1.0f);
                }

                ProcessZeroCrossing(sample);

                char? decodedChar = ProcessUartState();
                if (decodedChar.HasValue)
                {
                    byte b = (byte)decodedChar.Value;
                    newBytes.Add(b);
                    RawByteReceived?.Invoke(this, b);
                }
            }

            if (_clippingDetected)
            {
                Console.WriteLine("[WARNING] Input gain too high - signal clipping detected! Reduce gain.");
            }

            if (newBytes.Count > 0)
            {
                _byteBuffer.AddRange(newBytes);
                // Try to decode a packet from the buffer
                CustomProtocol.DecodedPacket? packet = CustomProtocol.DecodeAndConsume(_byteBuffer);
                if (packet != null)
                {
                    return packet;
                }
            }

            return null;
        }

        private void ProcessZeroCrossing(float sample)
        {
            _samplesSinceCrossing++;

            if ((sample > 0 && _lastSample <= 0) || (sample <= 0 && _lastSample > 0))
            {
                if (_samplesSinceCrossing > ZeroCrossingThreshold)
                {
                    _currentLevel = true;
                }
                else
                {
                    _currentLevel = false;
                }

                _samplesSinceCrossing = 0;
            }
            _lastSample = sample;
        }

        private char? ProcessUartState()
        {
            _samplesInCurrentState++;

            switch (_state)
            {
                case UartState.Idle:
                    if (!_currentLevel)
                    {
                        _state = UartState.StartBit;
                        _samplesInCurrentState = StartBitCompensation;
                    }
                    break;

                case UartState.StartBit:
                    if (_samplesInCurrentState >= SamplesPerBit / 2)
                    {
                        if (_currentLevel)
                        {
                            _state = UartState.Idle;
                        }
                        else
                        {
                            _state = UartState.DataBits;
                            _samplesInCurrentState -= SamplesPerBit / 2;
                            _bitIndex = 0;
                            _currentByte = 0;
                        }
                    }
                    break;

                case UartState.DataBits:
                    if (_samplesInCurrentState >= SamplesPerBit)
                    {
                        int bit = _currentLevel ? 1 : 0;
                        if (bit == 1) _currentByte |= (1 << _bitIndex);

                        _samplesInCurrentState -= SamplesPerBit;
                        _bitIndex++;

                        if (_bitIndex >= 8)
                        {
                            _state = UartState.StopBit;
                        }
                    }
                    break;

                case UartState.StopBit:
                    if (_samplesInCurrentState >= SamplesPerBit)
                    {
                        char c = (char)_currentByte;
                        _state = UartState.Idle;
                        _samplesInCurrentState = 0;
                        return c;
                    }
                    break;
            }

            return null;
        }
    }
}
