using RadioDataApp.Modem;
using RadioDataApp.Services;
using System;
using System.IO;
using System.Windows;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel
    {
        private void OnFileTransferProgressChanged(object? sender, double progress)
        {
            TransferProgress = progress * 100;
        }

        private void OnFileTransferDebugMessage(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += message + "\n";
            });
        }

        private void OnFileReceived(object? sender, string path)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string fileName = Path.GetFileName(path);
                string timestamp = DateTime.Now.ToString("yyyy/MM/dd 'at' h:mm tt");
                ChatLog += $"<< [Remote] RECEIVED FILE: {fileName} : Received {timestamp}\n";
                
                StatusMessage = $"File received: {fileName}";
                TransferStatus = "Receive Complete";
                IsReceiving = false;
                IsTransferring = false;
                DebugLog += $"<< File received: {fileName}\n";
                DebugLog += $"[FILE] Saved to: {path}\n";
            });
        }

        private void OnFileTransferTimeout(object? sender, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsReceiving = false;
                IsTransferring = false;
                DebugLog += "[TIMEOUT] " + message + "\n";
            });
        }

        private void OnFileOverwritePrompt(object? sender, FileTransferService.FileOverwriteEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string fileName = Path.GetFileName(e.FilePath);
                var result = MessageBox.Show(
                    $"The file '{fileName}' already exists.\n\nDo you want to overwrite it?\n\nYes = Overwrite existing file\nNo = Create new numbered file",
                    "File Already Exists",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                e.Overwrite = (result == MessageBoxResult.Yes);

                if (e.Overwrite)
                {
                    DebugLog += $"[FILE] Overwriting existing file: {fileName}\n";
                }
                else
                {
                    DebugLog += $"[FILE] Creating new numbered file for: {fileName}\n";
                }
            });
        }

        private void OnDangerousFileWarning(object? sender, FileTransferService.DangerousFileEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += $"[SECURITY WARNING] Potentially dangerous file type detected: {e.FileName}\n";
                
                var result = MessageBox.Show(
                    $"? SECURITY WARNING ?\n\n" +
                    $"The file '{e.FileName}' has a potentially dangerous file type ({e.FileExtension}).\n\n" +
                    $"This file type can execute code and may harm your computer if it contains malware.\n\n" +
                    $"Only save this file if:\n" +
                    $"• You trust the sender\n" +
                    $"• You know what this file is\n" +
                    $"• You plan to scan it with antivirus before opening\n\n" +
                    $"Do you want to save this file anyway?",
                    "Security Warning - Potentially Dangerous File",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                e.AllowSave = (result == MessageBoxResult.Yes);

                if (e.AllowSave)
                {
                    DebugLog += $"[SECURITY] User approved saving dangerous file: {e.FileName}\n";
                }
                else
                {
                    DebugLog += $"[SECURITY] User declined to save dangerous file: {e.FileName}\n";
                }
            });
        }

        private void OnRmsLevelDetected(object? sender, float rms)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (rms > 0.15f)
                {
                    DebugLog += "[? SIGNAL TOO STRONG: " + rms.ToString("F3") + "] Reduce input gain or system volume!\n";
                }
            });
        }

        private void OnChecksumFailed(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DebugLog += "\n[CHECKSUM FAIL] Packet corrupted!\n";
            });
        }
    }
}
