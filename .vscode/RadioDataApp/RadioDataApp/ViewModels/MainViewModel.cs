using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioDataApp.Modem;
using RadioDataApp.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.IO;
using NAudio.Wave;
using Microsoft.Win32;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly AudioService _audioService;
        private readonly AfskModem _modem;
        private readonly FileTransferService _fileTransferService;

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
        private string _debugLog = "Application Started...\n";

        [ObservableProperty]
        private double _transferProgress;

        [ObservableProperty]
        private string _transferStatus = "";

        [ObservableProperty]
        private bool _isTransferring;

        [ObservableProperty]
        private double _inputFrequency = 1000; // Far left when idle

        [ObservableProperty]
        private double _outputFrequency = 1000; // Far left when idle

        [ObservableProperty]
        private bool _compressImages = true; // Enable by default

        public MainViewModel()
        {
            _audioService = new AudioService();
            _modem = new AfskModem();
            _fileTransferService = new FileTransferService();

            _fileTransferService.ProgressChanged += (s, p) => TransferProgress = p * 100;
            _fileTransferService.FileReceived += (s, path) =>
            {
                Application.Current.Dispatcher.Invoke(() => DebugLog += $"[FILE RECEIVED] Saved to: {path}\n");
            };

            LoadDevices();
        }

        private void LoadDevices()
        {
            var inputs = _audioService.GetInputDevices();
            for (int i = 0; i < inputs.Count; i++) InputDevices.Add($"{i}: {inputs[i].ProductName}");

            var outputs = _audioService.GetOutputDevices();
            for (int i = 0; i < outputs.Count; i++) OutputDevices.Add($"{i}: {outputs[i].ProductName}");

            if (InputDevices.Count > 0) SelectedInputDeviceIndex = 0;
            if (OutputDevices.Count > 0) SelectedOutputDeviceIndex = 0;

            _audioService.AudioDataReceived += OnAudioDataReceived;
            _audioService.TransmissionCompleted += (s, e) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Don't reset during file transfer - let SendFile handle it
                    if (!IsTransferring)
                    {
                        StatusMessage = "Transmission Complete";
                        IsTransmitting = false;
                        OutputFrequency = 1000; // Reset to minimum (far left)
                    }
                });
            };

            try { _audioService.StartListening(SelectedInputDeviceIndex); } catch { }
        }

        private void OnAudioDataReceived(object? sender, byte[] audioData)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Detect frequency using zero-crossing rate
                int zeroCrossings = 0;
                short prevSample = 0;

                for (int i = 0; i < audioData.Length; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    if ((prevSample < 0 && sample >= 0) || (prevSample >= 0 && sample < 0))
                    {
                        zeroCrossings++;
                    }
                    prevSample = sample;
                }

                double durationSeconds = audioData.Length / 2.0 / 44100.0;
                double frequency = (zeroCrossings / 2.0) / durationSeconds;

                if (frequency >= 1000 && frequency <= 2400)
                {
                    InputFrequency = frequency;
                }

                CustomProtocol.DecodedPacket? packet = _modem.Demodulate(audioData);

                if (packet != null)
                {
                    if (packet.Type == CustomProtocol.PacketType.Text)
                    {
                        string text = System.Text.Encoding.ASCII.GetString(packet.Payload);
                        DebugLog += text + "\n";
                    }
                    else if (packet.Type == CustomProtocol.PacketType.FileHeader || packet.Type == CustomProtocol.PacketType.FileChunk)
                    {
                        _fileTransferService.HandlePacket(packet);
                    }
                }
            });
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartTransmissionCommand))]
        [NotifyCanExecuteChangedFor(nameof(SendFileCommand))]
        private bool _isTransmitting;

        [ObservableProperty]
        private string _messageToSend = "Hello World";

        private bool CanTransmit => !IsTransmitting;

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void StartTransmission()
        {
            IsTransmitting = true;
            StatusMessage = "Transmitting...";

            byte[] packet = CustomProtocol.Encode(MessageToSend);
            byte[] audioSamples = _modem.Modulate(packet);

            // Detect frequency
            int zeroCrossings = 0;
            short prevSample = 0;
            for (int i = 0; i < audioSamples.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(audioSamples, i);
                if ((prevSample < 0 && sample >= 0) || (prevSample >= 0 && sample < 0))
                {
                    zeroCrossings++;
                }
                prevSample = sample;
            }
            double durationSeconds = audioSamples.Length / 2.0 / 44100.0;
            OutputFrequency = (zeroCrossings / 2.0) / durationSeconds;

            _audioService.StartTransmitting(SelectedOutputDeviceIndex, audioSamples);
        }

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void SendFile()
        {
            var dialog = new OpenFileDialog { Title = "Select File to Send" };

            if (dialog.ShowDialog() == true)
            {
                var fileInfo = new System.IO.FileInfo(dialog.FileName);
                long fileSizeBytes = fileInfo.Length;

                // Calculate accurate time estimate accounting for all overhead
                int packetCount = (int)Math.Ceiling(fileSizeBytes / 200.0);
                double firstPacketTime = 3.4;  // 1s preamble + ~2s data + 0.3s postamble + 0.1s delay
                double otherPacketTime = 2.4;  // ~2s data + 0.3s postamble + 0.1s delay
                double estimatedSeconds = firstPacketTime + (packetCount - 1) * otherPacketTime;

                if (fileSizeBytes > 1024)
                {
                    string timeEstimate = estimatedSeconds < 60
                        ? $"{estimatedSeconds:F0} seconds"
                        : $"{estimatedSeconds / 60:F1} minutes";

                    var result = System.Windows.MessageBox.Show(
                        $"File size: {fileSizeBytes / 1024.0:F1} KB\n" +
                        $"Packets: {packetCount}\n" +
                        $"Estimated transmission time: {timeEstimate}\n\n" +
                        $"Continue?",
                        "File Transfer",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Information);

                    if (result != System.Windows.MessageBoxResult.Yes)
                        return;
                }

                IsTransmitting = true;
                IsTransferring = true;
                string fileName = System.IO.Path.GetFileName(dialog.FileName);
                StatusMessage = $"Sending: {fileName}";

                Task.Run(() =>
                {
                    var packets = _fileTransferService.PrepareFileForTransmission(dialog.FileName);
                    int totalPackets = packets.Count;
                    int currentPacket = 0;
                    DateTime startTime = DateTime.Now;

                    foreach (var packet in packets)
                    {
                        currentPacket++;
                        double progress = (double)currentPacket / totalPackets * 100;
                        TimeSpan elapsed = DateTime.Now - startTime;
                        double packetsPerSecond = currentPacket / elapsed.TotalSeconds;
                        int remainingPackets = totalPackets - currentPacket;
                        double etaSeconds = remainingPackets / packetsPerSecond;

                        // Only first packet needs preamble to wake VOX
                        bool needsPreamble = (currentPacket == 1);
                        byte[] audioSamples = _modem.Modulate(packet, needsPreamble);

                        // Detect frequency
                        int zeroCrossings = 0;
                        short prevSample = 0;
                        for (int i = 0; i < audioSamples.Length; i += 2)
                        {
                            short sample = BitConverter.ToInt16(audioSamples, i);
                            if ((prevSample < 0 && sample >= 0) || (prevSample >= 0 && sample < 0))
                            {
                                zeroCrossings++;
                            }
                            prevSample = sample;
                        }
                        double durationSeconds = audioSamples.Length / 2.0 / 44100.0;
                        double frequency = (zeroCrossings / 2.0) / durationSeconds;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TransferProgress = progress;
                            OutputFrequency = frequency;

                            string etaText = etaSeconds < 60
                                ? $"{etaSeconds:F0}s"
                                : $"{etaSeconds / 60:F1}m";

                            TransferStatus = $"Sending {fileName}: Packet {currentPacket}/{totalPackets} - ETA: {etaText}";
                        });

                        _audioService.StartTransmitting(SelectedOutputDeviceIndex, audioSamples);
                        Thread.Sleep(audioSamples.Length / 44100 * 1000 + 100);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"File sent: {fileName}";
                        TransferStatus = $"Completed: {fileName} ({totalPackets} packets)";
                        TransferProgress = 100;
                        IsTransmitting = false;
                        IsTransferring = false;
                        OutputFrequency = 1000; // Reset to minimum (far left)
                    });
                });
            }
        }
    }
}
