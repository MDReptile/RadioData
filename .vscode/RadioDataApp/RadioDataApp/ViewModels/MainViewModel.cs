using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioDataApp.Modem;
using RadioDataApp.Services;
using System.Collections.ObjectModel;
using System.Windows;
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
                    StatusMessage = "Transmission Complete";
                    IsTransmitting = false;
                    OutputVolume = 0;
                });
            };

            try { _audioService.StartListening(SelectedInputDeviceIndex); } catch { }
        }

        [ObservableProperty]
        private double _inputVolume;

        private void OnAudioDataReceived(object? sender, byte[] audioData)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Meter logic
                float maxSample = 0;
                for (int i = 0; i < audioData.Length; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    float normalized = Math.Abs(sample / 32768f);
                    if (normalized > maxSample) maxSample = normalized;
                }
                InputVolume = maxSample * 100;

                // Demodulate
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
        private bool _isTransmitting;

        [ObservableProperty]
        private double _outputVolume;

        [ObservableProperty]
        private string _messageToSend = "Hello World";

        private bool CanTransmit => !IsTransmitting;

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void StartTransmission()
        {
            IsTransmitting = true;
            OutputVolume = 100;
            StatusMessage = "Transmitting...";

            byte[] packet = CustomProtocol.Encode(MessageToSend);
            byte[] audioSamples = _modem.Modulate(packet);

            _audioService.StartTransmitting(SelectedOutputDeviceIndex, audioSamples);
        }

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void SendFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select File to Send"
            };

            if (dialog.ShowDialog() == true)
            {
                var fileInfo = new System.IO.FileInfo(dialog.FileName);
                long fileSizeBytes = fileInfo.Length;

                // Calculate estimated time (1000 baud ≈ 95 bytes/sec accounting for protocol overhead)
                double estimatedSeconds = fileSizeBytes / 95.0;

                // Warn if file is larger than ~1KB (32x32 PNG size threshold)
                if (fileSizeBytes > 1024)
                {
                    string timeEstimate;
                    if (estimatedSeconds < 60)
                    {
                        timeEstimate = $"{estimatedSeconds:F0} seconds";
                    }
                    else
                    {
                        timeEstimate = $"{estimatedSeconds / 60:F1} minutes";
                    }

                    var result = System.Windows.MessageBox.Show(
                        $"File size: {fileSizeBytes / 1024.0:F1} KB\n" +
                        $"Estimated transmission time: {timeEstimate}\n\n" +
                        $"This may take a while. Continue?",
                        "Large File Warning",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);

                    if (result != System.Windows.MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                IsTransmitting = true;
                OutputVolume = 100;
                StatusMessage = $"Sending file: {System.IO.Path.GetFileName(dialog.FileName)}";

                Task.Run(() =>
                {
                    var packets = _fileTransferService.PrepareFileForTransmission(dialog.FileName);

                    foreach (var packet in packets)
                    {
                        byte[] audioSamples = _modem.Modulate(packet);
                        _audioService.StartTransmitting(SelectedOutputDeviceIndex, audioSamples);

                        // Wait for transmission to complete
                        Thread.Sleep(audioSamples.Length / 44100 * 1000 + 100);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "File transmission complete";
                        IsTransmitting = false;
                        OutputVolume = 0;
                    });
                });
            }
        }
    }
}
