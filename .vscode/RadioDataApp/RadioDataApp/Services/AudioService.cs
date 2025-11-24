using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RadioDataApp.Services
{
    public class AudioService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;

        public event EventHandler<byte[]>? AudioDataReceived;

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

        public event EventHandler? TransmissionCompleted;

        public void StartTransmitting(int deviceNumber, byte[] audioData)
        {
            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
            }

            _waveOut = new WaveOutEvent
            {
                DeviceNumber = deviceNumber
            };

            _waveOut.PlaybackStopped += (s, e) => TransmissionCompleted?.Invoke(this, EventArgs.Empty);

            var waveProvider = new RawSourceWaveStream(new System.IO.MemoryStream(audioData), new WaveFormat(44100, 1));

            _waveOut.Init(waveProvider);
            _waveOut.Play();
        }

        public void Dispose()
        {
            StopListening();
            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
            }
        }
    }
}
