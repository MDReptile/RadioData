using System;
using System.Collections.Generic;
using System.Linq;

namespace RadioDataApp.Modem
{
    public class AfskModem
    {
        private const int SampleRate = 44100;
        private const int BaudRate = 50;
        private const int MarkFreq = 1200;
        private const int SpaceFreq = 2200;
        private const double SamplesPerBit = (double)SampleRate / BaudRate;

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

        public byte[] Modulate(byte[] data)
        {
            List<byte> samples = [];
            _phase = 0; // Reset phase for new transmission

            // Preamble (Sync) - 1000ms of Mark tones
            AddTone(samples, MarkFreq, 1000);

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

            // Postamble/Tail
            AddTone(samples, MarkFreq, 100);

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
                // Scale amplitude to 50%
                short sample = (short)(Math.Sin(_phase) * short.MaxValue * 0.5);
                byte[] bytes = BitConverter.GetBytes(sample);
                buffer.Add(bytes[0]);
                buffer.Add(bytes[1]);
                _phase += step;
                if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
            }
        }

        private List<byte> _byteBuffer = [];

        public byte[]? Demodulate(byte[] audioBytes)
        {
            List<byte> newBytes = [];

            for (int i = 0; i < audioBytes.Length; i += 2)
            {
                short sampleShort = BitConverter.ToInt16(audioBytes, i);
                float sample = sampleShort / 32768f;

                ProcessZeroCrossing(sample);

                char? decodedChar = ProcessUartState();
                if (decodedChar.HasValue)
                {
                    newBytes.Add((byte)decodedChar.Value);
                }
            }

            if (newBytes.Count > 0)
            {
                _byteBuffer.AddRange(newBytes);
                // Try to decode a packet from the buffer
                string? decodedMessage = CustomProtocol.DecodeAndConsume(_byteBuffer);
                if (decodedMessage != null)
                {
                    return System.Text.Encoding.ASCII.GetBytes(decodedMessage);
                }
            }

            return null;
        }

        private void ProcessZeroCrossing(float sample)
        {
            _samplesSinceCrossing++;

            // Detect crossing
            if ((sample > 0 && _lastSample <= 0) || (sample <= 0 && _lastSample > 0))
            {
                // Threshold: 14 samples
                if (_samplesSinceCrossing > 14)
                {
                    _currentLevel = true; // Mark
                }
                else
                {
                    _currentLevel = false; // Space
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
                    if (!_currentLevel) // Detected Space (Start Bit)
                    {
                        _state = UartState.StartBit;
                        // Compensate for detection latency (~10 samples for Space)
                        _samplesInCurrentState = -2;
                    }
                    break;

                case UartState.StartBit:
                    // Verify at middle of Start Bit
                    if (_samplesInCurrentState >= SamplesPerBit / 2)
                    {
                        if (_currentLevel) // False start
                        {
                            _state = UartState.Idle;
                        }
                        else
                        {
                            _state = UartState.DataBits;
                            // Align to center of bit
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
