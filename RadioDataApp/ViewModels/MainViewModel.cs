using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioDataApp.Modem;
using RadioDataApp.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.IO;
using NAudio.Wave;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System;
using System.Linq;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly AudioService _audioService;
        private readonly AfskModem _modem;
        private readonly FileTransferService _fileTransferService;
        private readonly ImageCompressionService _imageCompressionService;
        private readonly SettingsService _settingsService;

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

        private CancellationTokenSource? _receiveSilenceTimeout;
        private CancellationTokenSource? _transmissionCooldown;

        private bool CanTransmit => !IsTransmitting && !IsReceiving;

        // Debug flag for raw byte logging (set to true to enable)
        private const bool EnableRawByteLogging = false;

        public MainViewModel()
        {
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
            _fileTransferService.ProgressChanged += (s, p) => TransferProgress = p * 100;
            _fileTransferService.DebugMessage += (s, msg) => Application.Current.Dispatcher.Invoke(() => DebugLog += msg + "\n");
            _fileTransferService.FileReceived += (s, path) => Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"File received: {Path.GetFileName(path)}";
                TransferStatus = "Receive Complete";
                IsReceiving = false;
                IsTransferring = false;
                DebugLog += $"[FILE] Saved to: {path}\n";
            });
            _fileTransferService.TimeoutOccurred += (s, msg) => Application.Current.Dispatcher.Invoke(() =>
            {
                IsReceiving = false;
                IsTransferring = false;
                DebugLog += $"[TIMEOUT] {msg}\n";
            });

            // Hook up raw byte logging for debugging (optional)
            if (EnableRawByteLogging)
            {
                _modem.RawByteReceived += (s, b) => Application.Current.Dispatcher.Invoke(() =>
                {
                    // Log each byte on its own line for clarity
                    char c = (char)b;
                    string display = (c >= 32 && c <= 126) ? $"'{c}'" : $"[{b:X2}]";
                    DebugLog += $"[RAW BYTE] {display}\n";
                });
            }

            // Hook up RMS level logging for signal diagnostics
            _modem.RmsLevelDetected += (s, rms) => Application.Current.Dispatcher.Invoke(() =>
            {
                // Only warn if signal is too strong (causes distortion)
                if (rms > 0.15f)
                {
                    DebugLog += $"[⚠ SIGNAL TOO STRONG: {rms:F3}] Reduce input gain or system volume!\n";
                }
            });

            // Hook up checksum failure detection (now accurate!)
            _modem.ChecksumFailed += (s, e) => Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += $"\n[CHECKSUM FAIL] Packet corrupted!\n";
            });

            _audioService.AudioDataReceived += OnAudioDataReceived;
            _audioService.TransmissionCompleted += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!IsTransferring)
                    {
                        StatusMessage = "Transmission Complete";
                        
                        // Enforce minimum cooldown period (500ms) before allowing next transmission
                        _transmissionCooldown?.Cancel();
                        _transmissionCooldown = new CancellationTokenSource();
                        var token = _transmissionCooldown.Token;
                        
                        Task.Run(async () =>
                        {
                            await Task.Delay(500, token); // 500ms minimum cooldown
                            if (!token.IsCancellationRequested)
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    IsTransmitting = false;
                                });
                            }
                        }, token);
                    }
                });
            };

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

                    // Cancel any existing silence timeout
                    _receiveSilenceTimeout?.Cancel();
                    
                    // Start new silence timeout (2 seconds)
                    _receiveSilenceTimeout = new CancellationTokenSource();
                    var token = _receiveSilenceTimeout.Token;
                    Task.Run(async () =>
                    {
                        await Task.Delay(2000, token);
                        if (!token.IsCancellationRequested)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (!IsTransferring)
                                {
                                    IsReceiving = false;
                                }
                            });
                        }
                    }, token);
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
        }

        private CancellationTokenSource? _visualizationCts;

        private void StartVisualization(byte[] audioSamples)
        {
            _visualizationCts?.Cancel();
            _visualizationCts = new CancellationTokenSource();
            var token = _visualizationCts.Token;
            byte[] samplesCopy = audioSamples.ToArray();

            Task.Run(async () =>
            {
                int sampleRate = 44100;
                int bytesPerSample = 2;
                int updateIntervalMs = 50;
                int samplesPerUpdate = (sampleRate * updateIntervalMs) / 1000;
                int bytesPerUpdate = samplesPerUpdate * bytesPerSample;

                int offset = 0;

                while (offset < samplesCopy.Length && !token.IsCancellationRequested)
                {
                    int length = Math.Min(bytesPerUpdate, samplesCopy.Length - offset);
                    byte[] chunk = new byte[length];
                    Array.Copy(samplesCopy, offset, chunk, 0, length);

                    Application.Current.Dispatcher.Invoke(() => UpdateOutputMetrics(chunk));

                    offset += length;
                    await Task.Delay(updateIntervalMs);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    OutputVolume = 0;
                    OutputFrequency = 0;
                });
            }, token);
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

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void SendFile()
        {
            var dialog = new OpenFileDialog { Title = "Select File to Send" };
            if (dialog.ShowDialog() != true)
                return;

            string filePath = dialog.FileName;
            string fileName = Path.GetFileName(filePath);
            bool isCompressed = false;
            string tempPath = "";

            // Compression Logic
            if (CompressImages && ImageCompressionService.IsImageFile(filePath))
            {
                try
                {
                    StatusMessage = "Compressing Image...";
                    byte[] compressedData = _imageCompressionService.CompressImage(filePath);

                    tempPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(fileName) + ".cimg");
                    File.WriteAllBytes(tempPath, compressedData);

                    filePath = tempPath;
                    fileName = Path.GetFileName(tempPath); // .cimg
                    isCompressed = true;

                    DebugLog += $"[COMPRESSION] Reduced {new FileInfo(dialog.FileName).Length} bytes -> {compressedData.Length} bytes\n";
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

            if (fileSize > 1024 && !isCompressed) // Don't ask if we just compressed it, user knows
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

            Task.Run(() =>
            {
                try
                {
                    var packets = _fileTransferService.PrepareFileForTransmission(filePath);
                    int total = packets.Count;

                    int deviceIndex = _audioService.IsOutputLoopbackMode ? 0 : SelectedOutputDeviceIndex - 1;
                    _audioService.InitializeTransmission(deviceIndex);

                    for (int i = 0; i < total; i++)
                    {
                        bool preamble = i == 0;
                        int preambleDuration = preamble ? 1200 : 0;
                        var audio = _modem.Modulate(packets[i], preamble, preambleDuration);

                        _audioService.QueueAudio(audio);

                        Application.Current.Dispatcher.Invoke(() => UpdateOutputMetrics(audio));

                        double prog = (i + 1) / (double)total * 100;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TransferProgress = prog;
                            TransferStatus = $"Sending {fileName}: Packet {i + 1}/{total}";
                        });

                        if (_audioService.GetBufferedDuration().TotalSeconds > 10)
                        {
                            Thread.Sleep(1000);
                        }
                    }

                    while (_audioService.GetBufferedDuration().TotalMilliseconds > 0)
                    {
                        Thread.Sleep(100);
                    }

                    _audioService.StopTransmission();

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"File sent: {fileName}";
                        TransferStatus = $"Completed: {fileName} ({total} packets)";
                        TransferProgress = 100;
                        IsTransmitting = false;
                        IsTransferring = false;
                        OutputFrequency = 1000;
                        OutputVolume = 0;
                        DebugLog += "=== SEND COMPLETE ===\n";
                        DebugLog += "====================\n\n";
                    });
                }
                finally
                {
                    if (isCompressed && File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            });
        }
    }
}
