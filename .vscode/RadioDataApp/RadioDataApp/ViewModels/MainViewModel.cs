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

        public ObservableCollection<string> InputDevices { get; } = [];
        public ObservableCollection<string> OutputDevices { get; } = [];

        [ObservableProperty]
        private int _selectedInputDeviceIndex;

        partial void OnSelectedInputDeviceIndexChanged(int value)
        {
            try
            {
                _audioService.StartListening(value);
                StatusMessage = $"Listening on device {value}";
            }
            catch (System.Exception ex)
            {
                StatusMessage = $"Error listening: {ex.Message}";
            }
        }

        [ObservableProperty]
        private int _selectedOutputDeviceIndex;

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

        [ObservableProperty]
        private string _messageToSend = "Hello World";

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
            _settingsService.SaveSettings(new SettingsService.AppSettings { EncryptionKey = value });
            Console.WriteLine($"[Settings] Encryption key updated to: '{value}' ({value.Length} chars)");
        }

        [ObservableProperty]
        private bool _isTransmitting;

        [ObservableProperty]
        private bool _isReceiving;

        private bool CanTransmit => !IsTransmitting && !IsReceiving;

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
            CustomProtocol.EncryptionKey = _encryptionKey;
            Console.WriteLine($"[Settings] Loaded encryption key: {_encryptionKey}");

            // Wire up service events
            _fileTransferService.ProgressChanged += (s, p) => TransferProgress = p * 100;
            _fileTransferService.DebugMessage += (s, msg) => Application.Current.Dispatcher.Invoke(() => DebugLog += msg + "\n");
            _fileTransferService.FileReceived += (s, path) => Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += "\n=== FILE RECEIVED ===\n";
                DebugLog += $"Saved to: {path}\n";
                DebugLog += "====================\n\n";
                IsReceiving = false; // Reception complete
            });
            _fileTransferService.TimeoutOccurred += (s, msg) => Application.Current.Dispatcher.Invoke(() =>
            {
                IsReceiving = false; // Reception timed out
            });

            LoadDevices();
        }

        private void LoadDevices()
        {
            var inputs = _audioService.GetInputDevices();
            for (int i = 0; i < inputs.Count; i++)
                InputDevices.Add($"{i}: {inputs[i].ProductName}");

            var outputs = _audioService.GetOutputDevices();
            for (int i = 0; i < outputs.Count; i++)
                OutputDevices.Add($"{i}: {outputs[i].ProductName}");

            if (InputDevices.Count > 0) SelectedInputDeviceIndex = 0;
            if (OutputDevices.Count > 0) SelectedOutputDeviceIndex = 0;

            _audioService.AudioDataReceived += OnAudioDataReceived;
            _audioService.TransmissionCompleted += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!IsTransferring)
                    {
                        StatusMessage = "Transmission Complete";
                        IsTransmitting = false;
                        OutputFrequency = 1000;
                        OutputVolume = 0;
                    }
                });
            };

            try { _audioService.StartListening(SelectedInputDeviceIndex); } catch { }
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
                            IsReceiving = true; // Disable transmission during reception
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

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void StartTransmission()
        {
            IsTransmitting = true;
            StatusMessage = "Transmitting...";

            byte[] packet = CustomProtocol.Encode(MessageToSend);
            byte[] audioSamples = _modem.Modulate(packet);

            // Frequency detection for UI
            UpdateOutputMetrics(audioSamples);

            _audioService.StartTransmitting(SelectedOutputDeviceIndex, audioSamples);
        }

        private void UpdateOutputMetrics(byte[] audioSamples)
        {
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

            // Estimate time for 500 baud
            double firstPacketTime = 6.8;
            double otherPacketTime = 4.8;
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

                    // 1. Initialize continuous transmission
                    _audioService.InitializeTransmission(SelectedOutputDeviceIndex);

                    for (int i = 0; i < total; i++)
                    {
                        // 2. Modulate packet (Preamble only on first)
                        bool preamble = i == 0;
                        var audio = _modem.Modulate(packets[i], preamble);

                        // 3. Queue audio immediately
                        _audioService.QueueAudio(audio);

                        // Update Output Meters (approximate since it's queued)
                        Application.Current.Dispatcher.Invoke(() => UpdateOutputMetrics(audio));

                        // 4. Update UI
                        double prog = (i + 1) / (double)total * 100;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TransferProgress = prog;
                            TransferStatus = $"Sending {fileName}: Packet {i + 1}/{total}";
                        });

                        // Optional: Throttle loop slightly
                        if (_audioService.GetBufferedDuration().TotalSeconds > 10)
                        {
                            Thread.Sleep(1000);
                        }
                    }

                    // 5. Wait for playback to finish
                    while (_audioService.GetBufferedDuration().TotalMilliseconds > 0)
                    {
                        Thread.Sleep(100);
                    }

                    // 6. Stop transmission
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
                    // Cleanup temp file
                    if (isCompressed && File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            });
        }
    }
}
