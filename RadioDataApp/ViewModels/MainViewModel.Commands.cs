using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel
    {
        [RelayCommand]
        private void OpenReceivedFolder()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedFiles");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }

        [RelayCommand]
        private void ClearLogs()
        {
            var result = MessageBox.Show(
                "This will clear all chat history and system logs.\n\nThis action cannot be undone.\n\nContinue?",
                "Clear All Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                ChatLog = string.Empty;
                DebugLog = "RADIO_DATA_TERMINAL_INITIALIZED...\n";
                _settingsService.DeleteChatHistory();
                Console.WriteLine($"[ChatHistory] User cleared all logs");
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task RunAudioLoopbackTest()
        {
            DebugLog += "\n=== STARTING AUDIO LOOPBACK TEST ===\n";
            DebugLog += "This test will play audio and capture it\n";
            DebugLog += "to verify the full send/receive pipeline.\n";
            DebugLog += "Check Debug Output window for details.\n";
            DebugLog += "====================================\n\n";

            StatusMessage = "Running audio loopback test...";

            try
            {
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
