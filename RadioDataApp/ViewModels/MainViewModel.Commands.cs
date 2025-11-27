using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Linq;

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
        private void ClearChat()
        {
            var result = MessageBox.Show(
                "Clear all chat history?\n\nThis will remove all text messages from the chat window and delete the saved chat history file.\n\nThis action cannot be undone.",
                "Clear Chat History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                ChatLog = string.Empty;
                _settingsService.DeleteChatHistory();
                Console.WriteLine($"[Data] Chat history cleared");
            }
        }

        [RelayCommand]
        private void ClearSystemLog()
        {
            var result = MessageBox.Show(
                "Clear the system log?\n\nThis will remove all transmission, reception, and diagnostic messages from the system log window.\n\nThis action cannot be undone.",
                "Clear System Log",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                DebugLog = "RADIO_DATA_TERMINAL_INITIALIZED...\n";
                Console.WriteLine($"[Data] System log cleared");
            }
        }

        [RelayCommand]
        private void DeleteReceivedFiles()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedFiles");
            
            if (!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
            {
                MessageBox.Show(
                    "The ReceivedFiles folder is empty or does not exist.",
                    "No Files to Delete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int fileCount = Directory.GetFiles(path).Length;
            var result = MessageBox.Show(
                $"Delete all {fileCount} file(s) in the ReceivedFiles folder?\n\nThis will permanently delete:\n- All text files\n- All images\n- All other received files\n\nThis action cannot be undone!",
                "Delete Received Files",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    int deletedCount = 0;
                    foreach (string file in Directory.GetFiles(path))
                    {
                        File.Delete(file);
                        deletedCount++;
                    }
                    
                    MessageBox.Show(
                        $"Successfully deleted {deletedCount} file(s).",
                        "Files Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    Console.WriteLine($"[Data] Deleted {deletedCount} received files");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error deleting files:\n\n{ex.Message}",
                        "Delete Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    
                    Console.WriteLine($"[Data] Error deleting files: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void ResetSettings()
        {
            var result = MessageBox.Show(
                "Reset all settings to factory defaults?\n\nThis will reset:\n- Encryption key to 'RADIO'\n- Client name (new random name)\n- Message text box to 'Hello World'\n- All audio tuning parameters\n- Device selections to loopback mode\n\nChat history and received files will NOT be deleted.\n\nContinue?",
                "Reset Settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                EncryptionKey = "RADIO";
                ClientName = GenerateUniqueClientName();
                MessageToSend = "Hello World";
                
                InputGain = 1.0;
                OutputGain = 0.5;
                ZeroCrossingThreshold = 14;
                StartBitCompensation = -2.0;
                SquelchThreshold = 0.01;
                CompressImages = true;
                
                SelectedInputDeviceIndex = 0;
                SelectedOutputDeviceIndex = 0;
                
                SaveCurrentSettings();
                
                DebugLog += $"\n[SETTINGS RESET] All settings restored to defaults\n";
                DebugLog += $"New client name: {ClientName}\n\n";
                
                Console.WriteLine($"[Data] Settings reset to defaults");
                
                MessageBox.Show(
                    $"Settings have been reset to factory defaults.\n\nNew client name: {ClientName}",
                    "Settings Reset Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        private void ResetEverything()
        {
            var result = MessageBox.Show(
                "RESET EVERYTHING TO FACTORY DEFAULTS?\n\nThis will:\n• Clear all chat history\n• Clear system log\n• Delete all received files\n• Reset encryption key\n• Generate new client name\n• Reset all tuning parameters\n• Reset device selections\n\nTHIS CANNOT BE UNDONE!\n\nAre you absolutely sure?",
                "Reset Everything - Final Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                int filesDeleted = 0;
                string receivedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedFiles");
                
                if (Directory.Exists(receivedPath))
                {
                    try
                    {
                        foreach (string file in Directory.GetFiles(receivedPath))
                        {
                            File.Delete(file);
                            filesDeleted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Data] Error deleting files during reset: {ex.Message}");
                    }
                }
                
                ChatLog = string.Empty;
                _settingsService.DeleteChatHistory();
                
                DebugLog = "RADIO_DATA_TERMINAL_INITIALIZED...\n";
                DebugLog += $"\n[FACTORY RESET] Everything has been reset to defaults\n";
                
                EncryptionKey = "RADIO";
                ClientName = GenerateUniqueClientName();
                MessageToSend = "Hello World";
                
                InputGain = 1.0;
                OutputGain = 0.5;
                ZeroCrossingThreshold = 14;
                StartBitCompensation = -2.0;
                SquelchThreshold = 0.01;
                CompressImages = true;
                
                SelectedInputDeviceIndex = 0;
                SelectedOutputDeviceIndex = 0;
                
                SaveCurrentSettings();
                
                DebugLog += $"New client name: {ClientName}\n";
                DebugLog += $"Deleted {filesDeleted} received file(s)\n\n";
                
                Console.WriteLine($"[Data] Factory reset complete - deleted {filesDeleted} files");
                
                MessageBox.Show(
                    $"Factory reset complete!\n\n? Chat history cleared\n? System log cleared\n? {filesDeleted} file(s) deleted\n? All settings reset\n\nNew client name: {ClientName}",
                    "Reset Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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
