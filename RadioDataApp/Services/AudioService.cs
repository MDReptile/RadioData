#pragma warning disable CS8618
#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604

using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace RadioDataApp.Services
{
    public class AudioService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _bufferedWaveProvider;
        private DispatcherTimer? _loopbackTimer;
        private byte[]? _loopbackAudioData;
        private int _loopbackOffset;
        private const int LOOPBACK_CHUNK_SIZE = 4410;

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

            Console.WriteLine("[AudioService] ===== INITIALIZING OUTPUT TRANSMISSION =====");
            Console.WriteLine("[AudioService] Requested device index: " + deviceNumber);

            var outputs = GetOutputDevices();
            Console.WriteLine("[AudioService] Total output devices available: " + outputs.Count);
            
            for (int i = 0; i < outputs.Count; i++)
            {
                string marker = (i == deviceNumber) ? " <-- SELECTED" : "";
                Console.WriteLine($"[AudioService]   [{i}] {outputs[i].ProductName}{marker}");
            }

            if (deviceNumber >= 0 && deviceNumber < outputs.Count)
            {
                Console.WriteLine("[AudioService] ? Valid device index, opening: " + outputs[deviceNumber].ProductName);
            }
            else
            {
                Console.WriteLine("[AudioService] ? ERROR: Device index " + deviceNumber + " out of range (0-" + (outputs.Count - 1) + ")");
                Console.WriteLine("[AudioService] ? Audio will likely play on Windows default device!");
            }

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
            Console.WriteLine("[AudioService] ? Playback started on device index " + deviceNumber);
            Console.WriteLine("[AudioService] ==========================================");
        }

        public void QueueAudio(byte[] audioData)
        {
            if (IsOutputLoopbackMode)
            {
                Console.WriteLine("[AudioService] Output loopback mode - buffering " + audioData.Length + " bytes for simulated playback");
                return;
            }

            if (_bufferedWaveProvider != null)
            {
                _bufferedWaveProvider.AddSamples(audioData, 0, audioData.Length);
            }
        }

        public TimeSpan GetBufferedDuration()
        {
            if (IsOutputLoopbackMode && _loopbackAudioData != null)
            {
                int remainingBytes = _loopbackAudioData.Length - _loopbackOffset;
                double remainingSeconds = (double)remainingBytes / 2 / 44100;
                return TimeSpan.FromSeconds(remainingSeconds);
            }

            if (_bufferedWaveProvider != null)
            {
                return _bufferedWaveProvider.BufferedDuration;
            }
            return TimeSpan.Zero;
        }

        public void StopTransmission()
        {
            if (_loopbackTimer != null)
            {
                _loopbackTimer.Stop();
                _loopbackTimer = null;
            }

            if (IsOutputLoopbackMode)
            {
                Console.WriteLine("[AudioService] Output loopback mode - StopTransmission");
                _loopbackAudioData = null;
                _loopbackOffset = 0;
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
                Console.WriteLine("[AudioService] Output loopback mode - starting simulated transmission of " + audioData.Length + " bytes");
                _loopbackAudioData = audioData;
                _loopbackOffset = 0;

                _loopbackTimer = new DispatcherTimer();
                _loopbackTimer.Interval = TimeSpan.FromMilliseconds(100);
                _loopbackTimer.Tick += (s, e) =>
                {
                    if (_loopbackAudioData == null || _loopbackOffset >= _loopbackAudioData.Length)
                    {
                        _loopbackTimer.Stop();
                        _loopbackTimer = null;
                        Console.WriteLine("[AudioService] Loopback transmission complete");
                        var txHandler = TransmissionCompleted;
                        if (txHandler != null)
                        {
                            txHandler(this, EventArgs.Empty);
                        }
                        return;
                    }

                    int remaining = _loopbackAudioData.Length - _loopbackOffset;
                    int chunkSize = Math.Min(LOOPBACK_CHUNK_SIZE, remaining);
                    byte[] chunk = new byte[chunkSize];
                    Array.Copy(_loopbackAudioData, _loopbackOffset, chunk, 0, chunkSize);
                    _loopbackOffset += chunkSize;

                    var rxHandler = AudioDataReceived;
                    if (rxHandler != null)
                    {
                        rxHandler(this, chunk);
                    }
                };
                _loopbackTimer.Start();
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
