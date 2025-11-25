#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
#pragma warning disable CS8601 // Possible null reference assignment
#pragma warning disable CS8602 // Dereference of a possibly null reference
#pragma warning disable CS8603 // Possible null reference return
#pragma warning disable CS8604 // Possible null reference argument

using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace RadioDataApp.Services
{
    public class AudioService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;

        public event EventHandler<byte[]>? AudioDataReceived;
        public event EventHandler? TransmissionCompleted;

        public bool IsInputLoopbackMode { get; set; } = false;
        public bool IsOutputLoopbackMode { get; set; } = false;

        public static List<WaveInCapabilities> GetInputDevices()
        {
            var devices = new List<WaveInCapabilities>();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                devices.Add(WaveIn.GetCapabilities(i));
            }
            return devices;
        }

        public static List<WaveOutCapabilities> GetOutputDevices()
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
            if (IsInputLoopbackMode)
            {
                Console.WriteLine("[AudioService] Input loopback mode - skipping StartListening");
                return;
            }

            StopListening();

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(44100, 1)
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

            var handler = AudioDataReceived;
            if (handler != null)
            {
                handler(this, buffer);
            }
        }

        public void InitializeTransmission(int deviceNumber)
        {
            if (IsOutputLoopbackMode)
            {
                Console.WriteLine("[AudioService] Output loopback mode - InitializeTransmission (no-op)");
                return;
            }

            Console.WriteLine("[AudioService] InitializeTransmission called with device index: " + deviceNumber);

            var outputs = GetOutputDevices();
            if (deviceNumber >= 0 && deviceNumber < outputs.Count)
            {
                Console.WriteLine("[AudioService] Opening output device: " + outputs[deviceNumber].ProductName);
            }
            else
            {
                Console.WriteLine("[AudioService] ERROR: Device index " + deviceNumber + " out of range (0-" + (outputs.Count - 1) + ")");
            }

            // Always stop previous transmission to ensure clean state
            StopTransmission();

            _waveOut = new WaveOutEvent
            {
                DeviceNumber = deviceNumber
            };

            _bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(44100, 1))
            {
                BufferDuration = TimeSpan.FromMinutes(10),
                DiscardOnBufferOverflow = false
            };

            _waveOut.Init(_bufferedWaveProvider);
            _waveOut.Play();
            Console.WriteLine("[AudioService] Started playback on device " + deviceNumber);
        }

        public void QueueAudio(byte[] audioData)
        {
            if (IsOutputLoopbackMode)
            {
                Console.WriteLine("[AudioService] Output loopback mode - queuing " + audioData.Length + " bytes to demodulator");
                var handler = AudioDataReceived;
                if (handler != null)
                {
                    handler(this, audioData);
                }
                return;
            }

            if (_bufferedWaveProvider != null)
            {
                _bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length);
            }
        }

        public TimeSpan GetBufferedDuration()
        {
            if (_bufferedWaveProvider != null)
            {
                return _bufferedWaveProvider.BufferedDuration;
            }
            return TimeSpan.Zero;
        }

        public void StopTransmission()
        {
            if (IsOutputLoopbackMode)
            {
                Console.WriteLine("[AudioService] Output loopback mode - StopTransmission");
                var handler = TransmissionCompleted;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
                return;
            }

            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }
            _bufferedWaveProvider = null;

            var txHandler = TransmissionCompleted;
            if (txHandler != null)
            {
                txHandler(this, EventArgs.Empty);
            }
        }

        public void StartTransmitting(int deviceNumber, byte[] audioData)
        {
            if (IsOutputLoopbackMode)
            {
                Console.WriteLine("[AudioService] Output loopback mode - feeding " + audioData.Length + " bytes to demodulator");
                var rxHandler = AudioDataReceived;
                if (rxHandler != null)
                {
                    rxHandler(this, audioData);
                }
                var txHandler = TransmissionCompleted;
                if (txHandler != null)
                {
                    txHandler(this, EventArgs.Empty);
                }
                return;
            }

            InitializeTransmission(deviceNumber);
            QueueAudio(audioData);
        }

        public void Dispose()
        {
            StopListening();
            StopTransmission();
            GC.SuppressFinalize(this);
        }
    }
}
