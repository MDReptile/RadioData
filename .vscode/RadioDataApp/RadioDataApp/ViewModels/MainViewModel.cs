using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RadioDataApp.Modem;
using RadioDataApp.Services;
using System.Collections.ObjectModel;
using System.Windows;
using NAudio.Wave;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly AudioService _audioService;
        private readonly AfskModem _modem;

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

        public MainViewModel()
        {
            _audioService = new AudioService();
            _modem = new AfskModem();

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
            // Start listening on the selected device immediately (or add a button)
            // For now, let's start when the device is selected or just default to 0
            try { _audioService.StartListening(SelectedInputDeviceIndex); } catch { }
        }

        [ObservableProperty]
        private double _inputVolume;

        private void OnAudioDataReceived(object? sender, byte[] audioData)
        {
            // Run on UI thread to update DebugLog
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Calculate peak volume for the meter
                float maxSample = 0;
                for (int i = 0; i < audioData.Length; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    float normalized = Math.Abs(sample / 32768f);
                    if (normalized > maxSample) maxSample = normalized;
                }
                InputVolume = maxSample * 100;

                string result = _modem.Demodulate(audioData);
                if (!string.IsNullOrEmpty(result))
                {
                    DebugLog += result + "\n";
                    // Auto-scroll logic would go here
                }
            });
        }

        [RelayCommand]
        private void StartTransmission()
        {
            StatusMessage = "Transmitting...";
            byte[] data = System.Text.Encoding.ASCII.GetBytes("Hello World");
            byte[] audioSamples = _modem.Modulate(data);

            _audioService.StartTransmitting(SelectedOutputDeviceIndex, audioSamples);
            StatusMessage = "Transmission Complete";
        }

        [RelayCommand]
        private void StartTestTone()
        {
            StatusMessage = "Playing 5s Test Tone...";
            // Generate 5 seconds of tone
            byte[] audioSamples = _modem.GetTestTone(5000);

            _audioService.StartTransmitting(SelectedOutputDeviceIndex, audioSamples);
            StatusMessage = "Test Tone Complete";
        }
    }
}
