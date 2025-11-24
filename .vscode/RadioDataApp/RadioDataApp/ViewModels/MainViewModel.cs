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
        private string _transferStatus = string.Empty;

        [ObservableProperty]
        private bool _isTransferring;

        [ObservableProperty]
        private double _inputFrequency = 1000; // idle position

        [ObservableProperty]
        private double _outputFrequency = 1000; // idle position

        [ObservableProperty]
        private bool _compressImages = true;

        [ObservableProperty]
        private string _messageToSend = "Hello World";

        [ObservableProperty]
        private bool _isTransmitting;

        private bool CanTransmit => !IsTransmitting;

        public MainViewModel()
        {
            _audioService = new AudioService();
            _modem = new AfskModem();
            _fileTransferService = new FileTransferService();

            // Wire up service events
            _fileTransferService.ProgressChanged += (s, p) => TransferProgress = p * 100;
            _fileTransferService.DebugMessage += (s, msg) => Application.Current.Dispatcher.Invoke(() => DebugLog += msg + "\n");
            _fileTransferService.FileReceived += (s, path) => Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += "\n=== FILE RECEIVED ===\n";
                DebugLog += $"Saved to: {path}\n";
                DebugLog += "====================\n\n";
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
                    }
                });
            };

            try { _audioService.StartListening(SelectedInputDeviceIndex); } catch { }
        }

        private void OnAudioDataReceived(object? sender, byte[] audioData)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Frequency detection via zero‑crossing
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
                if (freq >= 1000 && freq <= 2400)
                    InputFrequency = freq;

                var packet = _modem.Demodulate(audioData);
                if (packet != null)
                {
                    switch (packet.Type)
                    {
                        case CustomProtocol.PacketType.Text:
                            DebugLog += System.Text.Encoding.ASCII.GetString(packet.Payload) + "\n";
                            break;
                        case CustomProtocol.PacketType.FileHeader:
                            DebugLog += "\n=== RECEIVING FILE ===\n";
                            _fileTransferService.HandlePacket(packet);
                            break;
                        case CustomProtocol.PacketType.FileChunk:
                            _fileTransferService.HandlePacket(packet);
                            break;
                    }
                }
            });
        }

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void StartTransmission()
        {
            IsTransmitting = true;
            StatusMessage = "Transmitting...";

            byte[] packet = CustomProtocol.Encode(MessageToSend);
            byte[] audioSamples = _modem.Modulate(packet);

            // Frequency detection for UI
            int zeroCrossings = 0;
            short prevSample = 0;
            for (int i = 0; i < audioSamples.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(audioSamples, i);
                if ((prevSample < 0 && sample >= 0) || (prevSample >= 0 && sample < 0))
                    zeroCrossings++;
                prevSample = sample;
            }
            double duration = audioSamples.Length / 2.0 / 44100.0;
            OutputFrequency = (zeroCrossings / 2.0) / duration;

            _audioService.StartTransmitting(SelectedOutputDeviceIndex, audioSamples);
        }

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void SendFile()
        {
            var dialog = new OpenFileDialog { Title = "Select File to Send" };
            if (dialog.ShowDialog() != true)
                return;

            var fileInfo = new FileInfo(dialog.FileName);
            long fileSize = fileInfo.Length;
            int packetCount = (int)Math.Ceiling(fileSize / 200.0);

            // Estimate time for 500 baud (double previous estimate)
            double firstPacketTime = 6.8; // preamble + data + postamble + delay
            double otherPacketTime = 4.8;
            double estimatedSeconds = firstPacketTime + (packetCount - 1) * otherPacketTime;

            if (fileSize > 1024)
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
            string fileName = Path.GetFileName(dialog.FileName);
            StatusMessage = $"Sending: {fileName}";

            // Debug header
            DebugLog += "\n=== SENDING FILE ===\n";
            DebugLog += $"File: {fileName}\n";
            DebugLog += $"Size: {fileSize / 1024.0:F1} KB\n";
            DebugLog += $"Packets: {packetCount}\n";
            DebugLog += $"Est. time: {estimatedSeconds:F0}s\n";
            DebugLog += "====================\n";

            Task.Run(() =>
            {
                var packets = _fileTransferService.PrepareFileForTransmission(dialog.FileName);
                var allAudio = new List<byte>();
                int total = packets.Count;
                for (int i = 0; i < total; i++)
                {
                    bool preamble = i == 0; // only first packet includes VOX preamble
                    var audio = _modem.Modulate(packets[i], preamble);
                    allAudio.AddRange(audio);

                    double prog = (i + 1) / (double)total * 100;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TransferProgress = prog;
                        TransferStatus = $"Sending {fileName}: Packet {i + 1}/{total}";
                    });
                }

                // Transmit concatenated audio
                _audioService.StartTransmitting(SelectedOutputDeviceIndex, allAudio.ToArray());

                // Approximate wait for playback to finish
                double totalSec = allAudio.Count / 2.0 / 44100.0;
                Thread.Sleep((int)(totalSec * 1000) + 200);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"File sent: {fileName}";
                    TransferStatus = $"Completed: {fileName} ({total} packets)";
                    TransferProgress = 100;
                    IsTransmitting = false;
                    IsTransferring = false;
                    OutputFrequency = 1000;
                    DebugLog += "=== SEND COMPLETE ===\n";
                    DebugLog += $"Total time: {totalSec:F1}s\n";
                    DebugLog += "====================\n\n";
                });
            });
        }
    }
}
