using CommunityToolkit.Mvvm.ComponentModel;
using RadioDataApp.Modem;
using RadioDataApp.Services;
using System;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel
    {
        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private string _debugLog = "RADIO_DATA_TERMINAL_INITIALIZED...\n";

        [ObservableProperty]
        private string _chatLog = "";

        partial void OnChatLogChanged(string value)
        {
            _settingsService.SaveChatHistory(value, EncryptionKey);
        }

        [ObservableProperty]
        private string _clientName = "";

        partial void OnClientNameChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ClientName = GenerateUniqueClientName();
                return;
            }

            if (value.Length > 10)
            {
                ClientName = value.Substring(0, 10);
                return;
            }

            SaveCurrentSettings();
        }

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
            catch (Exception ex)
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

        partial void OnMessageToSendChanged(string value)
        {
            StartTransmissionCommand.NotifyCanExecuteChanged();
        }

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
            if (string.IsNullOrEmpty(value))
            {
                EncryptionKey = "RADIO";
                return;
            }

            if (value.Length > 64)
            {
                EncryptionKey = value.Substring(0, 64);
                return;
            }

            CustomProtocol.EncryptionKey = value;
            Console.WriteLine($"[Settings] Encryption key updated to: '{value}' ({value.Length} chars)");
            SaveCurrentSettings();
        }

        [ObservableProperty]
        private bool _isTransmitting;

        partial void OnIsTransmittingChanged(bool value)
        {
            StartTransmissionCommand.NotifyCanExecuteChanged();
            SendFileCommand.NotifyCanExecuteChanged();
        }

        [ObservableProperty]
        private bool _isReceiving;

        partial void OnIsReceivingChanged(bool value)
        {
            StartTransmissionCommand.NotifyCanExecuteChanged();
            SendFileCommand.NotifyCanExecuteChanged();

            if (value)
            {
                StatusMessage = "Receiving...";
            }
            else if (!IsTransmitting)
            {
                StatusMessage = "Ready";
            }
        }
    }
}
