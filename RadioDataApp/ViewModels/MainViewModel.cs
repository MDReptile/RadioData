using CommunityToolkit.Mvvm.ComponentModel;
using RadioDataApp.Modem;
using RadioDataApp.Services;
using System;
using System.Collections.ObjectModel;

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

        public string AppVersion { get; } = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2) ?? "0.01"}";

        public ObservableCollection<string> InputDevices { get; } = [];
        public ObservableCollection<string> OutputDevices { get; } = [];

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

            if (string.IsNullOrWhiteSpace(settings.ClientName))
            {
                _clientName = GenerateUniqueClientName();
                SaveCurrentSettings();
            }
            else
            {
                _clientName = settings.ClientName;
            }

            CustomProtocol.EncryptionKey = _encryptionKey;
            _modem.InputGain = (float)_inputGain;
            _modem.ZeroCrossingThreshold = _zeroCrossingThreshold;
            _modem.StartBitCompensation = _startBitCompensation;
            _modem.SquelchThreshold = (float)_squelchThreshold;

            Console.WriteLine($"[Settings] Loaded client name: {_clientName}");
            Console.WriteLine($"[Settings] Loaded encryption key: {_encryptionKey}");
            Console.WriteLine($"[Settings] Loaded input gain: {_inputGain}x");
            Console.WriteLine($"[Settings] Loaded zero-crossing threshold: {_zeroCrossingThreshold}");
            Console.WriteLine($"[Settings] Loaded start bit compensation: {_startBitCompensation}");
            Console.WriteLine($"[Settings] Loaded squelch threshold: {_squelchThreshold:F3}");
            Console.WriteLine($"[Settings] Loaded compress images: {_compressImages}");

            // Load chat history (must be after encryption key is loaded)
            _chatLog = _settingsService.LoadChatHistory(_encryptionKey);
            if (!string.IsNullOrEmpty(_chatLog))
            {
                Console.WriteLine($"[ChatHistory] Restored previous chat history");
            }

            // Wire up service events
            _fileTransferService.ProgressChanged += OnFileTransferProgressChanged;
            _fileTransferService.DebugMessage += OnFileTransferDebugMessage;
            _fileTransferService.FileReceived += OnFileReceived;
            _fileTransferService.TimeoutOccurred += OnFileTransferTimeout;
            _fileTransferService.FileOverwritePrompt += OnFileOverwritePrompt;
            _fileTransferService.DangerousFileWarning += OnDangerousFileWarning;

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

        private string GenerateUniqueClientName()
        {
            long ticks = DateTime.UtcNow.Ticks;
            Random random = new Random((int)(ticks & 0xFFFFFFFF));
            
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            char[] name = new char[10];
            
            for (int i = 0; i < 10; i++)
            {
                name[i] = chars[random.Next(chars.Length)];
            }
            
            return new string(name);
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
                ClientName = ClientName,
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
    }
}
