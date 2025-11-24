using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RadioDataApp.Services
{
    public class AudioService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;

        public event EventHandler<byte[]>? AudioDataReceived;
        public event EventHandler? TransmissionCompleted;

        public List<WaveInCapabilities> GetInputDevices()
        {
            var devices = new List<WaveInCapabilities>();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                devices.Add(WaveIn.GetCapabilities(i));
            }
            return devices;
        }

        public List<WaveOutCapabilities> GetOutputDevices()
        {
            var devices = new List<WaveOutCapabilities>();
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                devices.Add(WaveOut.GetCapabilities(i));
            }
            return devices;
        }

        public void StartListening(int deviceNumber)
        {
            StopListening();

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(44100, 1) // 44.1kHz, Mono
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
        }

        public void StopListening()
        {
            if (_waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.Dispose();
                _waveIn = null;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            byte[] buffer = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, buffer, e.BytesRecorded);
            AudioDataReceived?.Invoke(this, buffer);
        }

        // --- Transmission Logic ---

        public void InitializeTransmission(int deviceNumber)
        {
            StopTransmission(); // Ensure clean state

            _waveOut = new WaveOutEvent
            {
                DeviceNumber = deviceNumber
            };

            _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(44100, 1))
            {
                BufferDuration = TimeSpan.FromMinutes(10), // Allow large buffer
                DiscardOnBufferOverflow = false
            };

            _waveOut.Init(_bufferedWaveProvider);
            _waveOut.Play();
        }

        public void QueueAudio(byte[] audioData)
        {
            if (_bufferedWaveProvider != null)
            {
                _bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length);
            }
        }

        public TimeSpan GetBufferedDuration()
        {
            return _bufferedWaveProvider?.BufferedDuration ?? TimeSpan.Zero;
        }

        public void StopTransmission()
        {
            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }
            _bufferedWaveProvider = null;
            TransmissionCompleted?.Invoke(this, EventArgs.Empty);
        }

        // Legacy method for single-shot transmission (kept for compatibility)
        public void StartTransmitting(int deviceNumber, byte[] audioData)
        {
            InitializeTransmission(deviceNumber);
            QueueAudio(audioData);

            // Wait for it to finish in a background thread so we don't block but can fire event
            new Thread(() =>
            {
                while (_bufferedWaveProvider != null && _bufferedWaveProvider.BufferedDuration.TotalMilliseconds > 0)
                {
                    Thread.Sleep(100);
                }
                StopTransmission();
            }).Start();
        }

        public void Dispose()
        {
            StopListening();
            StopTransmission();
        }
    }
}
