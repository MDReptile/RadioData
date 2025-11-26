using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioDataApp.Modem;
using RadioDataApp.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.IO;
using NAudio.Wave;
using Microsoft.Win32;
using System.Diagnostics;
using System;
using System.Windows.Threading;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly AudioService _audioService;
        private readonly AfskModem _modem;
        private readonly FileTransferService _fileTransferService;
        private readonly ImageCompressionService _imageCompressionService;
        private readonly SettingsService _settingsService;

        public static MainViewModel? Instance { get; private set; }

        [ObservableProperty]
        private string _statusMessage = "Ready";

        public string AppVersion { get; } = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.01"}";

        public ObservableCollection<string> InputDevices { get; } = [];
        public ObservableCollection<string> OutputDevices { get; } = [];

        [ObservableProperty]
        private int _selectedInputDeviceIndex;

        partial void OnSelectedInputDeviceIndexChanged(int value)
        {
            Console.WriteLine($"[DeviceSelection] Input device changed to index {value}");
            DebugLog += $"[DeviceSelection] Input index: {value}\n";

            if (value == 0)
            {
                _audioService.IsInputLoopbackMode = true;
                StatusMessage = "Input: Loopback mode (software)";
                Console.WriteLine($"[DeviceSelection] Input loopback mode enabled");
                DebugLog += "[DeviceSelection] Input loopback mode enabled\n";
                SaveCurrentSettings();
                return;
            }

            _audioService.IsInputLoopbackMode = false;
            try
            {
                int realDeviceIndex = value - 1;
                var inputs = AudioService.GetInputDevices();
                if (realDeviceIndex >= 0 && realDeviceIndex < inputs.Count)
                {
                    string deviceName = inputs[realDeviceIndex].ProductName;
                    Console.WriteLine($"[DeviceSelection] Input device {realDeviceIndex}: {deviceName}");
                    DebugLog += $"[DeviceSelection] Input device {realDeviceIndex}: {deviceName}\n";
                }

                _audioService.StartListening(realDeviceIndex);
                StatusMessage = $"Listening on device {realDeviceIndex}";
                SaveCurrentSettings();
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error listening: {ex.Message}";
                DebugLog += $"[ERROR] Listen failed: {ex.Message}\n";
            }
        }

        [ObservableProperty]
        private int _selectedOutputDeviceIndex;

        partial void OnSelectedOutputDeviceIndexChanged(int value)
        {
            Console.WriteLine($"[DeviceSelection] Output device changed to index {value}");
            DebugLog += $"[DeviceSelection] Output index: {value}\n";

            if (value == 0)
            {
                _audioService.IsOutputLoopbackMode = true;
                StatusMessage = "Output: Loopback mode (software)";
                Console.WriteLine($"[DeviceSelection] Output loopback mode enabled");
                DebugLog += "[DeviceSelection] Output loopback mode enabled\n";
                SaveCurrentSettings();
            }
            else
            {
                _audioService.IsOutputLoopbackMode = false;
                var outputs = AudioService.GetOutputDevices();
                int realDeviceIndex = value - 1;
                if (realDeviceIndex >= 0 && realDeviceIndex < outputs.Count)
                {
                    string deviceName = outputs[realDeviceIndex].ProductName;
                    StatusMessage = $"Output: {deviceName}";
                    Console.WriteLine($"[DeviceSelection] Real device index {realDeviceIndex}: {deviceName}");
                    DebugLog += $"[DeviceSelection] Device {realDeviceIndex}: {deviceName}\n";
                }
                else
                {
                    Console.WriteLine($"[DeviceSelection] ERROR: Real device index {realDeviceIndex} out of range (max {outputs.Count - 1})");
                    DebugLog += $"[ERROR] Device index {realDeviceIndex} out of range!\n";
                }
                SaveCurrentSettings();
            }
        }

        [ObservableProperty]
        private string _debugLog = "RADIO_DATA_TERMINAL_INITIALIZED...\n";

        [ObservableProperty]
        private double _transferProgress;

        [ObservableProperty]
        private string _transferStatus = string.Empty;

        [ObservableProperty]
        private bool _isTransferring;

        [ObservableProperty]
        private double _inputFrequency = 1000;

        [ObservableProperty]
        private double _outputFrequency = 1000;

        [ObservableProperty]
        private double _inputVolume = 0;

        [ObservableProperty]
        private double _outputVolume = 0;

        [ObservableProperty]
        private bool _compressImages = true;

        partial void OnCompressImagesChanged(bool value)
        {
            SaveCurrentSettings();
        }

        [ObservableProperty]
        private string _messageToSend = "Hello World";

        [ObservableProperty]
        private double _inputGain = 1.0;

        partial void OnInputGainChanged(double value)
        {
            _modem.InputGain = (float)value;
            Console.WriteLine($"[Settings] Input gain set to {value}x");
            SaveCurrentSettings();
        }

        [ObservableProperty]
        private int _zeroCrossingThreshold = 14;

        partial void OnZeroCrossingThresholdChanged(int value)
        {
            _modem.ZeroCrossingThreshold = value;
            Console.WriteLine($"[Settings] Zero-crossing threshold set to {value}");
            DebugLog += $"[Settings] Zero-crossing threshold: {value}\n";
            SaveCurrentSettings();
        }

        [ObservableProperty]
        private double _startBitCompensation = -2.0;

        partial void OnStartBitCompensationChanged(double value)
        {
            _modem.StartBitCompensation = value;
            Console.WriteLine($"[Settings] Start bit compensation set to {value}");
            DebugLog += $"[Settings] Start bit compensation: {value}\n";
            SaveCurrentSettings();
        }

        [ObservableProperty]
        private double _squelchThreshold = 0.01;

        partial void OnSquelchThresholdChanged(double value)
        {
            _modem.SquelchThreshold = (float)value;
            Console.WriteLine($"[Settings] Squelch threshold set to {value:F3}");
            DebugLog += $"[Settings] Squelch threshold: {value:F3}\n";
            SaveCurrentSettings();
        }

        [ObservableProperty]
        private string _encryptionKey = "RADIO";

        partial void OnEncryptionKeyChanged(string value)
        {
            // Validate length (1-64 characters)
            if (string.IsNullOrEmpty(value))
            {
                EncryptionKey = "RADIO"; // Reset to default if empty
                return;
            }

            if (value.Length > 64)
            {
                EncryptionKey = value.Substring(0, 64); // Truncate to max length
                return;
            }

            // Update protocol and save
            CustomProtocol.EncryptionKey = value;
            Console.WriteLine($"[Settings] Encryption key updated to: '{value}' ({value.Length} chars)");
            SaveCurrentSettings();
        }

        [ObservableProperty]
        private bool _isTransmitting;

        partial void OnIsTransmittingChanged(bool value)
        {
            // Notify UI that CanTransmit status may have changed
            StartTransmissionCommand.NotifyCanExecuteChanged();
            SendFileCommand.NotifyCanExecuteChanged();
        }

        [ObservableProperty]
        private bool _isReceiving;

        partial void OnIsReceivingChanged(bool value)
        {
            // Notify UI that CanTransmit status may have changed
            StartTransmissionCommand.NotifyCanExecuteChanged();
            SendFileCommand.NotifyCanExecuteChanged();

            // Update status message
            if (value)
            {
                StatusMessage = "Receiving...";
            }
            else if (!IsTransmitting)
            {
                StatusMessage = "Ready";
            }
        }


        private DispatcherTimer? _silenceTimer;

        private bool CanTransmit => !IsTransmitting && !IsReceiving;

        public MainViewModel()
        {
            Instance = this;
            
            _audioService = new AudioService();
            _modem = new AfskModem();
            _fileTransferService = new FileTransferService();
            _imageCompressionService = new ImageCompressionService();
            _settingsService = new SettingsService();

            // Load saved settings
            var settings = _settingsService.LoadSettings();
            _encryptionKey = settings.EncryptionKey;
            _inputGain = settings.InputGain;
            _zeroCrossingThreshold = settings.ZeroCrossingThreshold;
            _startBitCompensation = settings.StartBitCompensation;
            _squelchThreshold = settings.SquelchThreshold;
            _compressImages = settings.CompressImages;

            CustomProtocol.EncryptionKey = _encryptionKey;
            _modem.InputGain = (float)_inputGain;
            _modem.ZeroCrossingThreshold = _zeroCrossingThreshold;
            _modem.StartBitCompensation = _startBitCompensation;
            _modem.SquelchThreshold = (float)_squelchThreshold;

            Console.WriteLine($"[Settings] Loaded encryption key: {_encryptionKey}");
            Console.WriteLine($"[Settings] Loaded input gain: {_inputGain}x");
            Console.WriteLine($"[Settings] Loaded zero-crossing threshold: {_zeroCrossingThreshold}");
            Console.WriteLine($"[Settings] Loaded start bit compensation: {_startBitCompensation}");
            Console.WriteLine($"[Settings] Loaded squelch threshold: {_squelchThreshold:F3}");
            Console.WriteLine($"[Settings] Loaded compress images: {_compressImages}");

            // Wire up service events
            _fileTransferService.ProgressChanged += OnFileTransferProgressChanged;
            _fileTransferService.DebugMessage += OnFileTransferDebugMessage;
            _fileTransferService.FileReceived += OnFileReceived;
            _fileTransferService.TimeoutOccurred += OnFileTransferTimeout;

            // Hook up RMS level logging for signal diagnostics
            _modem.RmsLevelDetected += OnRmsLevelDetected;

            // Hook up checksum failure detection
            _modem.ChecksumFailed += OnChecksumFailed;

            // Hook up AudioService events
            _audioService.AudioDataReceived += OnAudioDataReceived;



            LoadDevices();

            // Load and apply saved device selections after devices are loaded
            _selectedInputDeviceIndex = settings.SelectedInputDeviceIndex;
            _selectedOutputDeviceIndex = settings.SelectedOutputDeviceIndex;

            // Validate that saved indices are still valid
            if (_selectedInputDeviceIndex >= InputDevices.Count)
                _selectedInputDeviceIndex = InputDevices.Count > 1 ? 1 : 0;
            if (_selectedOutputDeviceIndex >= OutputDevices.Count)
                _selectedOutputDeviceIndex = OutputDevices.Count > 1 ? 1 : 0;

            // Trigger the device selection handlers
            OnSelectedInputDeviceIndexChanged(_selectedInputDeviceIndex);
            OnSelectedOutputDeviceIndexChanged(_selectedOutputDeviceIndex);

            Console.WriteLine($"[Settings] Loaded input device index: {_selectedInputDeviceIndex}");
            Console.WriteLine($"[Settings] Loaded output device index: {_selectedOutputDeviceIndex}");
        }

        private void LoadDevices()
        {
            InputDevices.Clear();
            OutputDevices.Clear();

            // Add loopback option as first item
            InputDevices.Add("0: Loopback (Software)");
            OutputDevices.Add("0: Loopback (Software)");

            var inputs = AudioService.GetInputDevices();
            for (int i = 0; i < inputs.Count; i++)
                InputDevices.Add($"{i + 1}: {inputs[i].ProductName}");

            var outputs = AudioService.GetOutputDevices();
            for (int i = 0; i < outputs.Count; i++)
                OutputDevices.Add($"{i + 1}: {outputs[i].ProductName}");

            // Default to first real device (index 1 = first hardware device)
            if (InputDevices.Count > 1) SelectedInputDeviceIndex = 1;
            if (OutputDevices.Count > 1) SelectedOutputDeviceIndex = 1;
        }

        private void SaveCurrentSettings()
        {
            var settings = new SettingsService.AppSettings
            {
                EncryptionKey = EncryptionKey,
                SelectedInputDeviceIndex = SelectedInputDeviceIndex,
                SelectedOutputDeviceIndex = SelectedOutputDeviceIndex,
                InputGain = InputGain,
                ZeroCrossingThreshold = ZeroCrossingThreshold,
                StartBitCompensation = StartBitCompensation,
                SquelchThreshold = SquelchThreshold,
                CompressImages = CompressImages
            };
            _settingsService.SaveSettings(settings);
        }

        private void OnFileTransferProgressChanged(object? sender, double progress)
        {
            TransferProgress = progress * 100;
        }

        private void OnFileTransferDebugMessage(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += message + "\n";
            });
        }

        private void OnFileReceived(object? sender, string path)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = "File received: " + Path.GetFileName(path);
                TransferStatus = "Receive Complete";
                IsReceiving = false;
                IsTransferring = false;
                DebugLog += "[FILE] Saved to: " + path + "\n";
            });
        }

        private void OnFileTransferTimeout(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsReceiving = false;
                IsTransferring = false;
                DebugLog += "[TIMEOUT] " + message + "\n";
            });
        }

        private void OnRawByteReceived(object? sender, byte b)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                char c = (char)b;
                string display = (c >= 32 && c <= 126) ? "'" + c + "'" : "[" + b.ToString("X2") + "]";
                DebugLog += "[RAW BYTE] " + display + "\n";
            });
        }

        private void OnRmsLevelDetected(object? sender, float rms)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (rms > 0.15f)
                {
                    DebugLog += "[⚠ SIGNAL TOO STRONG: " + rms.ToString("F3") + "] Reduce input gain or system volume!\n";
                }
            });
        }

        private void OnChecksumFailed(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += "\n[CHECKSUM FAIL] Packet corrupted!\n";
            });
        }



        private void OnAudioDataReceived(object? sender, byte[] audioData)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 1. Calculate Volume (RMS)
                long sum = 0;
                for (int i = 0; i < audioData.Length; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    sum += sample * sample;
                }
                double rms = Math.Sqrt((double)sum / (audioData.Length / 2));
                InputVolume = Math.Min(1.0, rms / 10000.0); // Normalize roughly

                // 2. Frequency detection via zero‑crossing
                int zeroCrossings = 0;
                short prevSample = 0;
                for (int i = 0; i < audioData.Length; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    if ((prevSample < 0 && sample >= 0) || (prevSample >= 0 && sample < 0))
                        zeroCrossings++;
                    prevSample = sample;
                }
                double duration = audioData.Length / 2.0 / 44100.0;
                double freq = (zeroCrossings / 2.0) / duration;

                // Only update frequency if volume is sufficient (avoid noise)
                if (InputVolume > 0.05 && freq >= 500 && freq <= 3000)
                    InputFrequency = freq;

                // Check if there's signal above squelch threshold
                float normalizedRms = (float)(rms / 32768.0);
                if (normalizedRms >= _modem.SquelchThreshold)
                {
                    // Signal detected - set receiving flag
                    if (!IsReceiving && !IsTransferring)
                    {
                        IsReceiving = true;
                    }

                    // Initialize silence timer if needed
                    if (_silenceTimer == null)
                    {
                        _silenceTimer = new DispatcherTimer();
                        _silenceTimer.Interval = TimeSpan.FromSeconds(2);
                        _silenceTimer.Tick += (s, args) =>
                        {
                            if (!IsTransferring)
                            {
                                IsReceiving = false;
                            }
                            _silenceTimer.Stop();
                        };
                    }

                    // Reset silence timer
                    _silenceTimer.Stop();
                    _silenceTimer.Start();
                }

                // 3. Demodulate
                var packet = _modem.Demodulate(audioData);
                if (packet != null)
                {
                    switch (packet.Type)
                    {
                        case CustomProtocol.PacketType.Text:
                            DebugLog += "RX: " + System.Text.Encoding.ASCII.GetString(packet.Payload) + "\n";
                            break;
                        case CustomProtocol.PacketType.FileHeader:
                            DebugLog += "\n=== RECEIVING FILE ===\n";
                            IsTransferring = true; // Mark as file transfer in progress
                            _fileTransferService.HandlePacket(packet);
                            break;
                        case CustomProtocol.PacketType.FileChunk:
                            _fileTransferService.HandlePacket(packet);
                            break;
                    }
                }
            });
        }

        [RelayCommand]
        private void OpenReceivedFolder()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedFiles");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        [RelayCommand]
        private async Task RunAudioLoopbackTest()
        {
            DebugLog += "\n=== STARTING AUDIO LOOPBACK TEST ===\n";
            DebugLog += "This test will play audio and capture it\n";
            DebugLog += "to verify the full send/receive pipeline.\n";
            DebugLog += "Check Debug Output window for details.\n";
            DebugLog += "====================================\n\n";

            StatusMessage = "Running audio loopback test...";

            try
            {
                // Run the audio loopback test with currently selected devices
                await Tests.AudioLoopbackTest.RunAudioLoopbackTest(
                    SelectedOutputDeviceIndex,
                    SelectedInputDeviceIndex
                );

                DebugLog += "\n=== AUDIO TEST COMPLETE ===\n";
                DebugLog += "Check Debug Output for results.\n";
                DebugLog += "===========================\n\n";
                StatusMessage = "Audio loopback test complete";
            }
            catch (Exception ex)
            {
                DebugLog += $"\n[ERROR] Audio test failed: {ex.Message}\n\n";
                StatusMessage = "Audio loopback test failed";
            }
        }

        private DispatcherTimer? _transmissionMonitorTimer;

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void StartTransmission()
        {
            IsTransmitting = true;
            StatusMessage = "Transmitting...";

            DebugLog += $"TX: {MessageToSend}\n"; // Log sent message

            byte[] packet = CustomProtocol.Encode(MessageToSend);
            byte[] audioSamples = _modem.Modulate(packet);

            // Start visualization
            StartVisualization(audioSamples);

            // Check IsOutputLoopbackMode to determine device index
            int deviceIndex = _audioService.IsOutputLoopbackMode ? 0 : SelectedOutputDeviceIndex - 1;
            _audioService.StartTransmitting(deviceIndex, audioSamples);

            MessageToSend = string.Empty; // Clear input

            // Monitor buffer to reset IsTransmitting when done
            _transmissionMonitorTimer?.Stop();
            _transmissionMonitorTimer = new DispatcherTimer();
            _transmissionMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);

            bool hasStartedPlaying = false;

            _transmissionMonitorTimer.Tick += (s, e) =>
            {
                var duration = _audioService.GetBufferedDuration().TotalSeconds;

                if (duration > 0)
                {
                    hasStartedPlaying = true;
                }

                if (hasStartedPlaying && duration < 0.1)
                {
                    _transmissionMonitorTimer.Stop();
                    IsTransmitting = false;
                    StatusMessage = "Ready";
                }
            };
            _transmissionMonitorTimer.Start();
        }

        private DispatcherTimer? _visualizationTimer;

        private void StartVisualization(byte[] audioSamples)
        {
            _visualizationTimer?.Stop();

            byte[] samplesCopy = audioSamples.ToArray();
            int sampleRate = 44100;
            int bytesPerSample = 2;
            int updateIntervalMs = 50;
            int samplesPerUpdate = (sampleRate * updateIntervalMs) / 1000;
            int bytesPerUpdate = samplesPerUpdate * bytesPerSample;
            int offset = 0;

            _visualizationTimer = new DispatcherTimer();
            _visualizationTimer.Interval = TimeSpan.FromMilliseconds(updateIntervalMs);
            _visualizationTimer.Tick += (s, e) =>
            {
                if (offset >= samplesCopy.Length)
                {
                    _visualizationTimer.Stop();
                    // Reset meters
                    OutputVolume = 0;
                    OutputFrequency = 0;
                    return;
                }

                int length = Math.Min(bytesPerUpdate, samplesCopy.Length - offset);
                byte[] chunk = new byte[length];
                Array.Copy(samplesCopy, offset, chunk, 0, length);

                UpdateOutputMetrics(chunk);

                offset += length;
            };
            _visualizationTimer.Start();
        }

        private void UpdateOutputMetrics(byte[] audioSamples)
        {
            if (audioSamples.Length == 0) return;

            // Freq
            int zeroCrossings = 0;
            short prevSample = 0;
            long sum = 0;
            for (int i = 0; i < audioSamples.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(audioSamples, i);
                if ((prevSample < 0 && sample >= 0) || (prevSample >= 0 && sample < 0))
                    zeroCrossings++;
                prevSample = sample;
                sum += sample * sample;
            }
            double duration = audioSamples.Length / 2.0 / 44100.0;
            if (duration > 0)
                OutputFrequency = (zeroCrossings / 2.0) / duration;

            // Vol
            double rms = Math.Sqrt((double)sum / (audioSamples.Length / 2));
            OutputVolume = Math.Min(1.0, rms / 10000.0);
        }

        private DispatcherTimer? _fileTransferTimer;
        private List<byte[]>? _transferPackets;
        private int _transferIndex;
        private string? _transferTempPath;
        private bool _transferIsCompressed;

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void SendFile()
        {
            var dialog = new OpenFileDialog { Title = "Select File to Send" };
            if (dialog.ShowDialog() != true)
                return;

            SendFileWithPath(dialog.FileName);
        }

#if DEBUG
        /// <summary>
        /// Send a file directly by path, bypassing the file dialog.
        /// Used for automated testing.
        /// </summary>
        public void SendFileWithPath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                DebugLog += $"[ERROR] File not found: {filePath}\n";
                return;
            }

            if (!CanTransmit)
            {
                DebugLog += $"[ERROR] Cannot transmit: IsTransmitting={IsTransmitting}, IsReceiving={IsReceiving}\n";
                return;
            }

            ProcessFileSending(filePath);
        }
#else
        private void SendFileWithPath(string filePath)
        {
            ProcessFileSending(filePath);
        }
#endif

        private void ProcessFileSending(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            _transferIsCompressed = false;
            _transferTempPath = "";

            // Compression Logic
            if (CompressImages && ImageCompressionService.IsImageFile(filePath))
            {
                try
                {
                    StatusMessage = "Compressing Image...";
                    byte[] compressedData = _imageCompressionService.CompressImage(filePath);

                    _transferTempPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(fileName) + ".cimg");
                    File.WriteAllBytes(_transferTempPath, compressedData);

                    string originalPath = filePath;
                    filePath = _transferTempPath;
                    fileName = Path.GetFileName(_transferTempPath); // .cimg
                    _transferIsCompressed = true;

                    DebugLog += $"[COMPRESSION] Reduced {new FileInfo(originalPath).Length} bytes -> {compressedData.Length} bytes\n";
                }
                catch (Exception ex)
                {
                    DebugLog += $"[ERROR] Compression failed: {ex.Message}. Sending raw.\n";
                }
            }

            var fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;
            int packetCount = (int)Math.Ceiling(fileSize / 200.0);

            // Update time estimates for 250 baud
            double firstPacketTime = 13.5;
            double otherPacketTime = 9.5;
            double estimatedSeconds = firstPacketTime + (packetCount - 1) * otherPacketTime;

            if (fileSize > 1024 && !_transferIsCompressed) // Don't ask if we just compressed it, user knows
            {
                string timeStr = estimatedSeconds < 60 ? $"{estimatedSeconds:F0} seconds" : $"{estimatedSeconds / 60:F1} minutes";
                var result = MessageBox.Show(
                    $"File size: {fileSize / 1024.0:F1} KB\nPackets: {packetCount}\nEstimated time: {timeStr}\n\nContinue?",
                    "File Transfer",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            IsTransmitting = true;
            IsTransferring = true;
            StatusMessage = $"Sending: {fileName}";

            DebugLog += "\n=== SENDING FILE ===\n";
            DebugLog += $"File: {fileName}\n";
            DebugLog += $"Size: {fileSize / 1024.0:F1} KB\n";
            DebugLog += $"Packets: {packetCount}\n";
            DebugLog += $"Est. time: {estimatedSeconds:F0}s\n";
            DebugLog += "====================\n";

            // Prepare packets
            _transferPackets = _fileTransferService.PrepareFileForTransmission(filePath);
            _transferIndex = 0;

            // Initialize Transmission
            int deviceIndex = _audioService.IsOutputLoopbackMode ? 0 : SelectedOutputDeviceIndex - 1;
            _audioService.InitializeTransmission(deviceIndex);

            // Setup Timer
            _fileTransferTimer?.Stop();
            _fileTransferTimer = new DispatcherTimer();
            _fileTransferTimer.Interval = TimeSpan.FromMilliseconds(50); // Check frequently
            _fileTransferTimer.Tick += (s, e) =>
            {
                // 1. Flow Control
                if (_audioService.GetBufferedDuration().TotalSeconds > 10)
                {
                    return; // Buffer full, wait
                }

                // 2. Sending Packets
                if (_transferPackets != null && _transferIndex < _transferPackets.Count)
                {
                    var packet = _transferPackets[_transferIndex];
                    bool preamble = _transferIndex == 0;
                    int preambleDuration = preamble ? 1200 : 0;

                    // Modulate
                    var audio = _modem.Modulate(packet, preamble, preambleDuration);

                    // Queue
                    _audioService.QueueAudio(audio);

                    // Visualize (on UI thread, so simple call)
                    UpdateOutputMetrics(audio);

                    // Update Progress
                    _transferIndex++;
                    double prog = (double)_transferIndex / _transferPackets.Count * 100;
                    TransferProgress = prog;
                    TransferStatus = $"Sending {fileName}: Packet {_transferIndex}/{_transferPackets.Count}";

                    return;
                }

                // 3. Completion
                // Wait for buffer to drain
                if (_audioService.GetBufferedDuration().TotalSeconds > 0.1)
                {
                    return;
                }

                // Done
                _fileTransferTimer.Stop();
                _audioService.StopTransmission();

                StatusMessage = $"File sent: {fileName}";
                TransferStatus = $"Completed: {fileName} ({_transferPackets?.Count ?? 0} packets)";
                TransferProgress = 100;
                IsTransmitting = false;
                IsTransferring = false;
                OutputFrequency = 1000;
                OutputVolume = 0;
                DebugLog += "=== SEND COMPLETE ===\n";
                DebugLog += "====================\n\n";

                // Cleanup
                if (_transferIsCompressed && !string.IsNullOrEmpty(_transferTempPath) && File.Exists(_transferTempPath))
                {
                    try { File.Delete(_transferTempPath); } catch { }
                }
            };
            _fileTransferTimer.Start();
        }

        /// <summary>
        /// Programmatically add a log entry to the system log.
        /// Thread-safe for calling from external contexts (e.g., UI automation tests).
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="prefix">Optional prefix (e.g., "TEST", "SYSTEM"). If null, uses "[EXTERNAL]"</param>
        public void AddLogEntry(string message, string? prefix = null)
        {
            string logPrefix = prefix ?? "EXTERNAL";
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] [{logPrefix}] {message}\n";

            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                DebugLog += logEntry;
            }
            else
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    DebugLog += logEntry;
                });
            }
        }
    }
}
