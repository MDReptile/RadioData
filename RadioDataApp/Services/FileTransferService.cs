using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Timers;
using RadioDataApp.Modem;

namespace RadioDataApp.Services
{
    public class FileTransferService
    {
        private const int MaxChunkSize = 200;
        private const double FirstPacketTimeSeconds = 13.5;
        private const double OtherPacketTimeSeconds = 9.5;
        private const double TimeoutBufferSeconds = 15.0;
        private const double SilenceTimeoutSeconds = 20.0; // Timeout if no packets for 5s (dead air detection)
        private const double InitialChunkTimeoutSeconds = 20.0;

        private readonly ImageCompressionService _imageCompressionService = new();
        private readonly System.Timers.Timer _timeoutTimer;

        // Receive State
        private bool _isReceivingFile;
        private string _currentFileName = "";
        private int _totalFileSize;
        private byte[] _fileBuffer = []; // Fixed size buffer
        private HashSet<int> _receivedChunkIds = []; // Track unique chunks
        private int _expectedChunks;
        private bool _firstChunkReceived;

        // Timeout tracking
        private DateTime _receptionStartTime;
        private DateTime _lastPacketTime;
        private DateTime _lastChunkTime;
        private double _maxExpectedTimeSeconds;

        public event EventHandler<string>? FileReceived;
        public event EventHandler<double>? ProgressChanged;
        public event EventHandler<string>? DebugMessage;
        public event EventHandler<string>? TimeoutOccurred;
        public event EventHandler<FileOverwriteEventArgs>? FileOverwritePrompt;
        public event EventHandler<DangerousFileEventArgs>? DangerousFileWarning;

        // Public state for debugging
        public int ReceivedChunks => _receivedChunkIds.Count;
        public int ExpectedChunks => _expectedChunks;
        public string CurrentFileName => _currentFileName;
        public bool IsReceivingFile => _isReceivingFile;

        public void NotifyAudioReceived()
        {
            if (_isReceivingFile)
            {
                _lastPacketTime = DateTime.Now;
            }
        }

        public class FileOverwriteEventArgs : EventArgs
        {
            public string FilePath { get; set; } = "";
            public bool Overwrite { get; set; }
        }

        public class DangerousFileEventArgs : EventArgs
        {
            public string FileName { get; set; } = "";
            public string FileExtension { get; set; } = "";
            public bool AllowSave { get; set; }
        }

        // List of potentially dangerous file extensions
        private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".msi", ".msp", ".scr", ".hta", ".cpl", ".msc",
            ".jar", ".ps1", ".ps1xml", ".ps2", ".ps2xml", ".psc1", ".psc2",
            ".reg", ".dll", ".sys", ".drv", ".ocx", ".app", ".deb", ".rpm",
            ".sh", ".bash", ".py", ".rb", ".pl", ".php", ".asp", ".aspx"
        };

        private static bool IsDangerousFileType(string fileName)
        {
            string extension = Path.GetExtension(fileName).ToLower();
            return DangerousExtensions.Contains(extension);
        }

        public FileTransferService()
        {
            // Initialize timeout timer (check every 500ms for faster dead air detection)
            _timeoutTimer = new System.Timers.Timer(500);
            _timeoutTimer.Elapsed += OnTimeoutCheck;
            _timeoutTimer.AutoReset = true;
        }

        private void OnTimeoutCheck(object? sender, ElapsedEventArgs e)
        {
            if (!_isReceivingFile)
            {
                _timeoutTimer.Stop();
                return;
            }

            DateTime now = DateTime.Now;
            double elapsedTotal = (now - _receptionStartTime).TotalSeconds;
            double elapsedSinceLastPacket = (now - _lastPacketTime).TotalSeconds;

            double timeoutThreshold = _firstChunkReceived ? SilenceTimeoutSeconds : InitialChunkTimeoutSeconds;

            if (elapsedSinceLastPacket > timeoutThreshold)
            {
                string phase = _firstChunkReceived ? "between chunks" : "waiting for first chunk";
                HandleTimeout($"Timeout: No signal for {elapsedSinceLastPacket:F1}s ({phase} - transmission stopped)");
                return;
            }

            if (elapsedTotal > _maxExpectedTimeSeconds)
            {
                HandleTimeout($"Timeout: Expected completion in {_maxExpectedTimeSeconds:F1}s, but elapsed {elapsedTotal:F1}s");
                return;
            }
        }

        private void HandleTimeout(string message)
        {
            Console.WriteLine($"[FileTransfer] {message}");
            _timeoutTimer.Stop();
            _isReceivingFile = false;

            // List missing chunks
            var missingChunks = new List<int>();
            for (int i = 0; i < _expectedChunks; i++)
            {
                if (!_receivedChunkIds.Contains(i))
                {
                    missingChunks.Add(i);
                }
            }

            string missingChunksStr = missingChunks.Count > 0 
                ? $"Missing chunks: {string.Join(", ", missingChunks)}" 
                : "All chunks received but not processed";

            TimeoutOccurred?.Invoke(this, message);
            DebugMessage?.Invoke(this, $"\n=== {message} ===\nReceived {_receivedChunkIds.Count}/{_expectedChunks} chunks\n{missingChunksStr}\n");
        }

        public List<byte[]> PrepareFileForTransmission(string filePath)
        {
            List<byte[]> packets = [];
            byte[] fileData = File.ReadAllBytes(filePath);
            string fileName = Path.GetFileName(filePath);

            // 1. Create Header Packet
            // Format: [NameLength(1), NameBytes(...), Size(4)]
            List<byte> headerPayload = [];
            byte[] nameBytes = Encoding.ASCII.GetBytes(fileName);
            headerPayload.Add((byte)nameBytes.Length);
            headerPayload.AddRange(nameBytes);
            headerPayload.AddRange(BitConverter.GetBytes(fileData.Length));

            packets.Add(CustomProtocol.Encode(headerPayload.ToArray(), CustomProtocol.PacketType.FileHeader));

            // 2. Create Chunk Packets
            // Format: [SeqID(2), Data...]
            int chunks = (int)Math.Ceiling((double)fileData.Length / MaxChunkSize);

            for (int i = 0; i < chunks; i++)
            {
                int offset = i * MaxChunkSize;
                int size = Math.Min(MaxChunkSize, fileData.Length - offset);

                List<byte> chunkPayload = [];
                chunkPayload.AddRange(BitConverter.GetBytes((short)i)); // SeqID

                byte[] chunkData = new byte[size];
                Array.Copy(fileData, offset, chunkData, 0, size);
                chunkPayload.AddRange(chunkData);

                packets.Add(CustomProtocol.Encode(chunkPayload.ToArray(), CustomProtocol.PacketType.FileChunk));
            }

            return packets;
        }

        public void HandlePacket(CustomProtocol.DecodedPacket packet)
        {
            if (packet.Type == CustomProtocol.PacketType.FileHeader)
            {
                // Check if we're already receiving a file - if so, the previous transmission failed
                if (_isReceivingFile)
                {
                    string oldFileName = _currentFileName;
                    int oldReceived = _receivedChunkIds.Count;
                    int oldExpected = _expectedChunks;
                    
                    Console.WriteLine($"[FileTransfer] WARNING: New FileHeader received while already receiving '{oldFileName}'");
                    Console.WriteLine($"[FileTransfer] Previous transmission incomplete: {oldReceived}/{oldExpected} chunks");
                    
                    string warningMsg = $"[TRANSFER FAILED] Previous file '{oldFileName}' incomplete ({oldReceived}/{oldExpected} chunks)\n" +
                                       $"Starting new transmission...";
                    DebugMessage?.Invoke(this, warningMsg);
                    
                    // Clean up old transfer
                    _timeoutTimer.Stop();
                    _isReceivingFile = false;
                }
                
                try
                {
                    int nameLen = packet.Payload[0];
                    _currentFileName = Encoding.ASCII.GetString(packet.Payload, 1, nameLen);
                    _totalFileSize = BitConverter.ToInt32(packet.Payload, 1 + nameLen);

                    _fileBuffer = new byte[_totalFileSize];
                    _receivedChunkIds.Clear();
                    _isReceivingFile = true;
                    _firstChunkReceived = false;
                    _expectedChunks = (int)Math.Ceiling((double)_totalFileSize / MaxChunkSize);

                    // Initialize timeout tracking
                    _receptionStartTime = DateTime.Now;
                    _lastPacketTime = DateTime.Now;
                    _lastChunkTime = DateTime.Now;
                    _maxExpectedTimeSeconds = FirstPacketTimeSeconds + (_expectedChunks * OtherPacketTimeSeconds) + TimeoutBufferSeconds;
                    _timeoutTimer.Start();

                    double estimatedAudioDuration = 1.2 + (_expectedChunks * 8.3) + 0.8;
                    string debugMsg = $">> File: {_currentFileName}\n" +
                                    $"Size: {_totalFileSize / 1024.0:F1} KB\n" +
                                    $"Packets: {_expectedChunks}\n" +
                                    $"Est. audio: {estimatedAudioDuration:F0}s\n" +
                                    $"====================\n" +
                                    $"[RX TIMING] Header received at {DateTime.Now:HH:mm:ss.fff}";
                    DebugMessage?.Invoke(this, debugMsg);

                    Console.WriteLine($"[FileTransfer] Starting receive: {_currentFileName} ({_totalFileSize} bytes, {_expectedChunks} chunks, timeout: {_maxExpectedTimeSeconds:F1}s)");
                    ProgressChanged?.Invoke(this, 0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileTransfer] Error parsing header: {ex.Message}");
                    _isReceivingFile = false;
                    _timeoutTimer.Stop();
                }
            }
            else if (packet.Type == CustomProtocol.PacketType.FileChunk && _isReceivingFile)
            {
                DateTime now = DateTime.Now;
                _lastPacketTime = now;
                
                if (!_firstChunkReceived)
                {
                    double delayAfterHeader = (now - _receptionStartTime).TotalSeconds;
                    _firstChunkReceived = true;
                    Console.WriteLine($"[FileTransfer] First chunk received, switching to {SilenceTimeoutSeconds}s inter-chunk timeout");
                    DebugMessage?.Invoke(this, $"[RX TIMING] First chunk after {delayAfterHeader:F2}s");
                }

                try
                {
                    // Parse Chunk
                    short seqId = BitConverter.ToInt16(packet.Payload, 0);
                    byte[] data = new byte[packet.Payload.Length - 2];
                    Array.Copy(packet.Payload, 2, data, 0, data.Length);

                    // Place in correct position
                    int offset = seqId * MaxChunkSize;
                    if (offset + data.Length <= _fileBuffer.Length)
                    {
                        Array.Copy(data, 0, _fileBuffer, offset, data.Length);

                        if (_receivedChunkIds.Add(seqId))
                        {
                            double timeSinceLastChunk = (now - _lastChunkTime).TotalSeconds;
                            double totalElapsed = (now - _receptionStartTime).TotalSeconds;
                            _lastChunkTime = now;
                            
                            double progress = (double)_receivedChunkIds.Count / _expectedChunks;
                            ProgressChanged?.Invoke(this, progress);

                            string debugMsg = $"Chunk #{seqId} received | {_receivedChunkIds.Count}/{_expectedChunks} ({progress:P0}) | Gap: {timeSinceLastChunk:F2}s | Total: {totalElapsed:F2}s | Time: {now:HH:mm:ss.fff}";
                            DebugMessage?.Invoke(this, debugMsg);
                            Console.WriteLine($"[FileTransfer] Received chunk {seqId}. Progress: {progress:P0}");

                            if (_receivedChunkIds.Count >= _expectedChunks)
                            {
                                FinishReception();
                            }
                        }
                        else
                        {
                            DebugMessage?.Invoke(this, $"[RX TIMING] Duplicate chunk #{seqId} ignored");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileTransfer] Error parsing chunk: {ex.Message}");
                }
            }
            else if (packet.Type == CustomProtocol.PacketType.FileChunk && !_isReceivingFile)
            {
                // Received a chunk without a header - log warning
                Console.WriteLine($"[FileTransfer] WARNING: Received FileChunk without active transfer (orphaned chunk)");
                DebugMessage?.Invoke(this, "[WARNING] Received file chunk without header - ignoring");
            }
        }

        private void FinishReception()
        {
            double totalTime = (DateTime.Now - _receptionStartTime).TotalSeconds;
            Console.WriteLine($"[FileTransfer] FinishReception called. Chunks received: {_receivedChunkIds.Count}/{_expectedChunks}");
            DebugMessage?.Invoke(this, $"[RX TIMING] Transfer complete in {totalTime:F2}s\n" +
                                      $"<< File received: {_currentFileName}\n" +
                                      $"====================\n");
            
            _isReceivingFile = false;
            _timeoutTimer.Stop();

            // Check for dangerous file types before writing to disk
            if (IsDangerousFileType(_currentFileName))
            {
                Console.WriteLine($"[FileTransfer] Detected potentially dangerous file type: {_currentFileName}");
                var dangerArgs = new DangerousFileEventArgs 
                { 
                    FileName = _currentFileName,
                    FileExtension = Path.GetExtension(_currentFileName),
                    AllowSave = false 
                };
                
                DangerousFileWarning?.Invoke(this, dangerArgs);
                
                if (!dangerArgs.AllowSave)
                {
                    Console.WriteLine($"[FileTransfer] User declined to save dangerous file: {_currentFileName}");
                    DebugMessage?.Invoke(this, $"[SECURITY] File transfer cancelled: {_currentFileName} (potentially dangerous file type)");
                    return;
                }
                
                Console.WriteLine($"[FileTransfer] User approved saving dangerous file: {_currentFileName}");
                DebugMessage?.Invoke(this, $"[SECURITY] User approved saving: {_currentFileName}");
            }

            string receiveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedFiles");
            Console.WriteLine($"[FileTransfer] Target directory: {receiveDir}");

            if (!Directory.Exists(receiveDir))
            {
                Directory.CreateDirectory(receiveDir);
                Console.WriteLine($"[FileTransfer] Created directory: {receiveDir}");
            }

            string savePath = Path.Combine(receiveDir, _currentFileName);
            Console.WriteLine($"[FileTransfer] Initial save path: {savePath}");

            if (File.Exists(savePath))
            {
                var args = new FileOverwriteEventArgs { FilePath = savePath, Overwrite = false };
                FileOverwritePrompt?.Invoke(this, args);

                if (!args.Overwrite)
                {
                    savePath = GetUniqueFilePath(receiveDir, _currentFileName);
                    Console.WriteLine($"[FileTransfer] Creating new file: {savePath}");
                }
                else
                {
                    Console.WriteLine($"[FileTransfer] Overwriting existing file: {savePath}");
                }
            }

            Console.WriteLine($"[FileTransfer] Final save path: {savePath}");

            try
            {
                File.WriteAllBytes(savePath, _fileBuffer);
                Console.WriteLine($"[FileTransfer] Successfully wrote {_fileBuffer.Length} bytes to {savePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileTransfer] ERROR writing file: {ex.Message}");
                DebugMessage?.Invoke(this, $"ERROR: Failed to save file - {ex.Message}");
                return;
            }

            // Decompression Logic
            if (Path.GetExtension(savePath).ToLower() == ".cimg")
            {
                Console.WriteLine($"[FileTransfer] Detected .cimg file, attempting decompression...");
                try
                {
                    string decompressedPath = Path.ChangeExtension(savePath, ".png");

                    if (File.Exists(decompressedPath))
                    {
                        var args = new FileOverwriteEventArgs { FilePath = decompressedPath, Overwrite = false };
                        FileOverwritePrompt?.Invoke(this, args);

                        if (!args.Overwrite)
                        {
                            decompressedPath = GetUniqueFilePath(receiveDir, Path.GetFileName(decompressedPath));
                        }
                    }

                    _imageCompressionService.DecompressImage(_fileBuffer, decompressedPath);

                    Console.WriteLine($"[FileTransfer] Decompressed to {decompressedPath}");
                    FileReceived?.Invoke(this, decompressedPath);

                    File.Delete(savePath);
                    Console.WriteLine($"[FileTransfer] Deleted temporary .cimg file");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileTransfer] Decompression failed: {ex.Message}");
                    FileReceived?.Invoke(this, savePath);
                }
            }
            else
            {
                Console.WriteLine($"[FileTransfer] Regular file, invoking FileReceived event");
                FileReceived?.Invoke(this, savePath);
            }

            ProgressChanged?.Invoke(this, 1.0);
            Console.WriteLine($"[FileTransfer] FinishReception completed successfully");
        }

        private string GetUniqueFilePath(string directory, string fileName)
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;
            string newPath = Path.Combine(directory, fileName);

            while (File.Exists(newPath))
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}_{counter++}{ext}");
            }

            return newPath;
        }
    }
}
