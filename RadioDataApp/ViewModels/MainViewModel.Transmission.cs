using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using RadioDataApp.Modem;
using RadioDataApp.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel
    {
        private DispatcherTimer? _transmissionMonitorTimer;
        private DispatcherTimer? _visualizationTimer;
        private string? _transferTempPath;
        private bool _transferIsCompressed;
        private DateTime _transferStartTime;

        private bool CanTransmit => !IsTransmitting && !IsReceiving;

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void StartTransmission()
        {
            if (string.IsNullOrWhiteSpace(MessageToSend))
            {
                return;
            }

            IsTransmitting = true;
            StatusMessage = "Transmitting...";

            string timestamp = DateTime.Now.ToString("yyyy/MM/dd 'at' h:mm tt");
            ChatLog += $">> [{ClientName}] {MessageToSend} : Sent {timestamp}\n";

            byte[] packet = CustomProtocol.Encode(MessageToSend, ClientName);
            byte[] audioSamples = _modem.Modulate(packet);

            StartVisualization(audioSamples);

            int deviceIndex = _audioService.IsOutputLoopbackMode ? 0 : SelectedOutputDeviceIndex - 1;
            
            if (!_audioService.IsOutputLoopbackMode)
            {
                var outputs = AudioService.GetOutputDevices();
                if (deviceIndex >= 0 && deviceIndex < outputs.Count)
                {
                    DebugLog += $"[TX] Using output device [{deviceIndex}]: {outputs[deviceIndex].ProductName}\n";
                }
            }
            
            _audioService.StartTransmitting(deviceIndex, audioSamples);

            MessageToSend = string.Empty;

            _transmissionMonitorTimer?.Stop();
            _transmissionMonitorTimer = new DispatcherTimer();
            _transmissionMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);

            bool hasStartedPlaying = false;

            _transmissionMonitorTimer.Tick += (s, e) =>
            {
                var duration = _audioService.GetBufferedDuration().TotalSeconds;

                if (duration > 0)
                {
                    hasStartedPlaying = true;
                }

                if (hasStartedPlaying && duration < 0.1)
                {
                    _transmissionMonitorTimer.Stop();
                    IsTransmitting = false;
                    StatusMessage = "Ready";
                }
            };
            _transmissionMonitorTimer.Start();
        }

        private void StartVisualization(byte[] audioSamples)
        {
            _visualizationTimer?.Stop();

            byte[] samplesCopy = audioSamples.ToArray();
            int sampleRate = 44100;
            int bytesPerSample = 2;
            int updateIntervalMs = 50;
            int samplesPerUpdate = (sampleRate * updateIntervalMs) / 1000;
            int bytesPerUpdate = samplesPerUpdate * bytesPerSample;
            int offset = 0;

            _visualizationTimer = new DispatcherTimer();
            _visualizationTimer.Interval = TimeSpan.FromMilliseconds(updateIntervalMs);
            _visualizationTimer.Tick += (s, e) =>
            {
                if (offset >= samplesCopy.Length)
                {
                    _visualizationTimer.Stop();
                    OutputVolume = 0;
                    OutputFrequency = 0;
                    return;
                }

                int length = Math.Min(bytesPerUpdate, samplesCopy.Length - offset);
                byte[] chunk = new byte[length];
                Array.Copy(samplesCopy, offset, chunk, 0, length);

                UpdateOutputMetrics(chunk);

                offset += length;
            };
            _visualizationTimer.Start();
        }

        private void UpdateOutputMetrics(byte[] audioSamples)
        {
            if (audioSamples.Length == 0) return;

            int zeroCrossings = 0;
            short prevSample = 0;
            long sum = 0;
            for (int i = 0; i < audioSamples.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(audioSamples, i);
                if ((prevSample < 0 && sample >= 0) || (prevSample >= 0 && sample < 0))
                    zeroCrossings++;
                prevSample = sample;
                sum += sample * sample;
            }
            double duration = audioSamples.Length / 2.0 / 44100.0;
            if (duration > 0)
                OutputFrequency = (zeroCrossings / 2.0) / duration;

            double rms = Math.Sqrt((double)sum / (audioSamples.Length / 2));
            OutputVolume = Math.Min(1.0, rms / 10000.0);
        }

        [RelayCommand(CanExecute = nameof(CanTransmit))]
        private void SendFile()
        {
            var dialog = new OpenFileDialog { Title = "Select File to Send" };
            if (dialog.ShowDialog() != true)
                return;

            SendFileWithPath(dialog.FileName);
        }

#if DEBUG
        public void SendFileWithPath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                DebugLog += $"[ERROR] File not found: {filePath}\n";
                return;
            }

            if (!CanTransmit)
            {
                DebugLog += $"[ERROR] Cannot transmit: IsTransmitting={IsTransmitting}, IsReceiving={IsReceiving}\n";
                return;
            }

            ProcessFileSending(filePath);
        }
#else
        private void SendFileWithPath(string filePath)
        {
            ProcessFileSending(filePath);
        }
#endif

        private void ProcessFileSending(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            _transferIsCompressed = false;
            _transferTempPath = "";

            if (CompressImages && ImageCompressionService.IsImageFile(filePath))
            {
                try
                {
                    StatusMessage = "Compressing Image...";
                    byte[] compressedData = _imageCompressionService.CompressImage(filePath);

                    _transferTempPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(fileName) + ".cimg");
                    File.WriteAllBytes(_transferTempPath, compressedData);

                    string originalPath = filePath;
                    filePath = _transferTempPath;
                    fileName = Path.GetFileName(_transferTempPath);
                    _transferIsCompressed = true;

                    DebugLog += $"[COMPRESSION] Reduced {new FileInfo(originalPath).Length} bytes -> {compressedData.Length} bytes\n";
                }
                catch (Exception ex)
                {
                    DebugLog += $"[ERROR] Compression failed: {ex.Message}. Sending raw.\n";
                }
            }

            var fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;
            int packetCount = (int)Math.Ceiling(fileSize / 200.0) + 1;

            double estimatedSeconds = 1.2 + ((packetCount - 1) * 4.2) + 0.8;
            string timeStr = FormatDuration(estimatedSeconds);

            // Warn about very long transfers (>5 minutes)
            if (estimatedSeconds > 300)
            {
                var result = System.Windows.MessageBox.Show(
                    $"VERY LONG TRANSFER TIME\n\n" +
                    $"File: {fileName}\n" +
                    $"Size: {fileSize / 1024.0:F1} KB\n" +
                    $"Packets: {packetCount}\n" +
                    $"Estimated time: {timeStr}\n\n" +
                    $"?? This transfer may:\n" +
                    $"• Take a very long time to complete\n" +
                    $"• Fail if radio connection is interrupted\n" +
                    $"• Exceed audio buffer capacity (>10 min may fail)\n\n" +
                    $"Consider compressing the file or sending a smaller version.\n\n" +
                    $"Continue anyway?",
                    "Long Transfer Warning",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                    return;
            }
            else if (fileSize > 1024 && !_transferIsCompressed)
            {
                var result = System.Windows.MessageBox.Show(
                    $"File Transfer Confirmation\n\n" +
                    $"File: {fileName}\n" +
                    $"Size: {fileSize / 1024.0:F1} KB\n" +
                    $"Packets: {packetCount}\n" +
                    $"Estimated time: {timeStr}\n\n" +
                    $"Continue?",
                    "File Transfer",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);
                if (result != System.Windows.MessageBoxResult.Yes)
                    return;
            }

            IsTransmitting = true;
            IsTransferring = true;
            StatusMessage = $"Generating audio for {fileName}...";

            DebugLog += "\n=== SENDING FILE ===\n";
            DebugLog += $">> File: {fileName}\n";
            DebugLog += $"Size: {fileSize / 1024.0:F1} KB\n";
            DebugLog += $"Packets: {packetCount}\n";
            DebugLog += $"Est. time: {timeStr}\n";
            DebugLog += "====================\n";

            _transferStartTime = DateTime.Now;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var packets = _fileTransferService.PrepareFileForTransmission(filePath);
                    
                    List<byte> completeAudio = new List<byte>();
                    
                    for (int i = 0; i < packets.Count; i++)
                    {
                        bool isFirst = i == 0;
                        bool isLast = i == packets.Count - 1;
                        int preambleDuration = isFirst ? 1200 : 0;
                        bool includePostamble = isLast;
                        bool resetPhase = isFirst;
                        
                        var packetAudio = _modem.Modulate(packets[i], isFirst, preambleDuration, includePostamble, resetPhase);
                        completeAudio.AddRange(packetAudio);
                    }

                    byte[] finalAudio = completeAudio.ToArray();
                    double totalDurationSeconds = finalAudio.Length / 88200.0;
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Sending: {fileName}";
                        DebugLog += $"[TX] Generated continuous audio: {FormatDuration(totalDurationSeconds)} | {packets.Count} packets with phase continuity\n";
                        
                        int deviceIndex = _audioService.IsOutputLoopbackMode ? 0 : SelectedOutputDeviceIndex - 1;
                        
                        if (!_audioService.IsOutputLoopbackMode)
                        {
                            var outputs = AudioService.GetOutputDevices();
                            if (deviceIndex >= 0 && deviceIndex < outputs.Count)
                            {
                                DebugLog += $"[TX] Using output device [{deviceIndex}]: {outputs[deviceIndex].ProductName}\n";
                            }
                        }
                        
                        _audioService.StartTransmitting(deviceIndex, finalAudio);

                        StartVisualization(finalAudio);
                        
                        _transmissionMonitorTimer?.Stop();
                        _transmissionMonitorTimer = new DispatcherTimer();
                        _transmissionMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);
                        
                        bool hasStartedPlaying = false;
                        
                        _transmissionMonitorTimer.Tick += (s, e) =>
                        {
                            var duration = _audioService.GetBufferedDuration().TotalSeconds;
                            
                            if (duration > 0)
                            {
                                hasStartedPlaying = true;
                            }
                            
                            double totalDuration = finalAudio.Length / 88200.0;
                            double elapsed = totalDuration - duration;
                            double progress = (elapsed / totalDuration) * 100;
                            
                            TransferProgress = Math.Min(100, progress);
                            TransferStatus = $"Sending {fileName}: {progress:F0}%";
                            
                            if (hasStartedPlaying && duration < 0.1)
                            {
                                _transmissionMonitorTimer.Stop();
                                
                                double totalTransferTime = (DateTime.Now - _transferStartTime).TotalSeconds;
                                string timestamp = DateTime.Now.ToString("yyyy/MM/dd 'at' h:mm tt");
                                ChatLog += $">> [{ClientName}] SENT FILE: {fileName} : Sent {timestamp}\n";
                                
                                StatusMessage = $"File sent: {fileName}";
                                TransferStatus = $"Completed: {fileName} ({packets.Count} packets)";
                                TransferProgress = 100;
                                IsTransmitting = false;
                                IsTransferring = false;
                                OutputFrequency = 1000;
                                OutputVolume = 0;
                                DebugLog += $"[TX TIMING] Transfer complete in {FormatDuration(totalTransferTime)}\n";
                                DebugLog += ">> File send complete\n";
                                DebugLog += "====================\n\n";
                                
                                if (_transferIsCompressed && !string.IsNullOrEmpty(_transferTempPath) && File.Exists(_transferTempPath))
                                {
                                    try { File.Delete(_transferTempPath); } catch { }
                                }
                            }
                        };
                        _transmissionMonitorTimer.Start();
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        DebugLog += $"[ERROR] Audio generation failed: {ex.Message}\n";
                        StatusMessage = "Transfer failed";
                        IsTransmitting = false;
                        IsTransferring = false;
                    });
                }
            });
        }

        private string FormatDuration(double seconds)
        {
            if (seconds < 60)
            {
                return $"{seconds:F0}s";
            }
            else if (seconds < 3600)
            {
                int minutes = (int)(seconds / 60);
                int secs = (int)(seconds % 60);
                return $"{minutes}m {secs}s";
            }
            else
            {
                int hours = (int)(seconds / 3600);
                int minutes = (int)((seconds % 3600) / 60);
                return $"{hours}h {minutes}m";
            }
        }
    }
}
