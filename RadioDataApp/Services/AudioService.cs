#pragma warning disable CS8618
#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604

using NAudio.Wave;
using NAudio.CoreAudioApi;
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

        public static float GetOutputDeviceVolume(int waveOutDeviceIndex)
        {
            try
            {
                // Get the WaveOut device name
                var waveOutCaps = WaveOut.GetCapabilities(waveOutDeviceIndex);
                string targetDeviceName = waveOutCaps.ProductName;

                LogService.Debug($"[AudioService] Looking for volume of WaveOut device: {targetDeviceName}");

                // Find matching MMDevice by name
                var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in devices)
                {
                    // MMDevice names often contain the same product name
                    if (device.FriendlyName.Contains(targetDeviceName) || targetDeviceName.Contains(device.FriendlyName))
                    {
                        float volume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
                        LogService.Debug($"[AudioService] Matched MMDevice: {device.FriendlyName}, Volume: {(int)(volume * 100)}%");
                        return volume;
                    }
                }

                LogService.Debug($"[AudioService] Could not find matching MMDevice for: {targetDeviceName}");
            }
            catch (Exception ex)
            {
                LogService.Error($"[AudioService] Error getting device volume: {ex.Message}");
            }

            return 1.0f; // Return 100% if unable to get volume
        }

        public void StartListening(int deviceNumber)
        {
            if (IsInputLoopbackMode)
            {
                LogService.Debug("[AudioService] Input loopback mode - skipping StartListening");
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
                LogService.Debug("[AudioService] Output loopback mode - InitializeTransmission (no-op)");
                return;
            }

            LogService.Log("===== INITIALIZING OUTPUT TRANSMISSION =====", "AUDIO");
            LogService.Debug("[AudioService] Requested device index: " + deviceNumber);

            var outputs = GetOutputDevices();
            LogService.Debug("[AudioService] Total output devices available: " + outputs.Count);

            for (int i = 0; i < outputs.Count; i++)
            {
                string marker = (i == deviceNumber) ? " <-- SELECTED" : "";
                LogService.Debug($"[AudioService]   [{i}] {outputs[i].ProductName}{marker}");
            }

            if (deviceNumber >= 0 && deviceNumber < outputs.Count)
            {
                LogService.Log("Valid device index, opening: " + outputs[deviceNumber].ProductName, "AUDIO");
            }
            else
            {
                LogService.Error("ERROR: Device index " + deviceNumber + " out of range (0-" + (outputs.Count - 1) + ")");
                LogService.Error("Audio will likely play on Windows default device!");
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
            LogService.Log("Playback started on device index " + deviceNumber, "AUDIO");
            LogService.Log("==========================================", "AUDIO");
        }

        public void QueueAudio(byte[] audioData)
        {
            if (IsOutputLoopbackMode)
            {
                LogService.Debug("[AudioService] Output loopback mode - buffering " + audioData.Length + " bytes for simulated playback");
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
                LogService.Debug("[AudioService] Output loopback mode - StopTransmission");
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
                LogService.Debug("[AudioService] Output loopback mode - starting simulated transmission of " + audioData.Length + " bytes");
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
                        LogService.Debug("[AudioService] Loopback transmission complete");
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
