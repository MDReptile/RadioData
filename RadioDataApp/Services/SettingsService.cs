using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RadioDataApp.Services
{
    public class SettingsService
    {
        private const string SettingsFileName = "RadioData.settings.json";
        private const string ChatHistoryFileName = "RadioData.chat.enc";
        private readonly string _settingsPath;
        private readonly string _chatHistoryPath;

        public class AppSettings
        {
            public string EncryptionKey { get; set; } = "RADIO";
            public string ClientName { get; set; } = "";
            public int SelectedInputDeviceIndex { get; set; } = 1;
            public int SelectedOutputDeviceIndex { get; set; } = 1;
            public double InputGain { get; set; } = 1.0;
            public int ZeroCrossingThreshold { get; set; } = 14;
            public double StartBitCompensation { get; set; } = -2.0;
            public double SquelchThreshold { get; set; } = 0.01;
            public bool CompressImages { get; set; } = true;
        }

        public SettingsService()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RadioData"
            );

            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }

            _settingsPath = Path.Combine(appDataPath, SettingsFileName);
            _chatHistoryPath = Path.Combine(appDataPath, ChatHistoryFileName);
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings] Error loading settings: {ex.Message}");
            }

            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                Console.WriteLine($"[Settings] Saved to {_settingsPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Settings] Error saving settings: {ex.Message}");
            }
        }

        public void SaveChatHistory(string chatLog, string encryptionKey)
        {
            try
            {
                if (string.IsNullOrEmpty(chatLog))
                {
                    // If chat log is empty, delete the file if it exists
                    if (File.Exists(_chatHistoryPath))
                    {
                        File.Delete(_chatHistoryPath);
                        Console.WriteLine($"[ChatHistory] Deleted empty chat history");
                    }
                    return;
                }

                byte[] chatBytes = Encoding.UTF8.GetBytes(chatLog);
                byte[] encrypted = XorEncrypt(chatBytes, encryptionKey);
                File.WriteAllBytes(_chatHistoryPath, encrypted);
                Console.WriteLine($"[ChatHistory] Saved encrypted chat history ({chatBytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatHistory] Error saving chat history: {ex.Message}");
            }
        }

        public string LoadChatHistory(string encryptionKey)
        {
            try
            {
                if (File.Exists(_chatHistoryPath))
                {
                    byte[] encrypted = File.ReadAllBytes(_chatHistoryPath);
                    byte[] decrypted = XorEncrypt(encrypted, encryptionKey);
                    string chatLog = Encoding.UTF8.GetString(decrypted);
                    Console.WriteLine($"[ChatHistory] Loaded chat history ({encrypted.Length} bytes encrypted)");
                    return chatLog;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatHistory] Error loading chat history: {ex.Message}");
            }

            return string.Empty;
        }

        public void DeleteChatHistory()
        {
            try
            {
                if (File.Exists(_chatHistoryPath))
                {
                    File.Delete(_chatHistoryPath);
                    Console.WriteLine($"[ChatHistory] Deleted chat history file");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ChatHistory] Error deleting chat history: {ex.Message}");
            }
        }

        private byte[] XorEncrypt(byte[] data, string key)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);
            byte[] result = new byte[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ keyBytes[i % keyBytes.Length]);
            }

            return result;
        }
    }
}
