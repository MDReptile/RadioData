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
        private const int MaxChunkSize = 200; // Payload size limit
        private const double AvgPacketTimeSeconds = 4.8; // Average time per packet at 250 baud
        private const double SilenceTimeoutSeconds = 10.0; // Timeout if no packets for 10s

        private readonly ImageCompressionService _imageCompressionService = new();
        private readonly System.Timers.Timer _timeoutTimer;

        // Receive State
        private bool _isReceivingFile;
        private string _currentFileName = "";
        private int _totalFileSize;
        private byte[] _fileBuffer = []; // Fixed size buffer
        private HashSet<int> _receivedChunkIds = []; // Track unique chunks
        private int _expectedChunks;

        // Timeout tracking
        private DateTime _receptionStartTime;
        private DateTime _lastPacketTime;
        private double _maxExpectedTimeSeconds;

        public event EventHandler<string>? FileReceived;
        public event EventHandler<double>? ProgressChanged;
        public event EventHandler<string>? DebugMessage;
        public event EventHandler<string>? TimeoutOccurred;

        // Public state for debugging
        public int ReceivedChunks => _receivedChunkIds.Count;
        public int ExpectedChunks => _expectedChunks;
        public string CurrentFileName => _currentFileName;
        public bool IsReceivingFile => _isReceivingFile;

        public FileTransferService()
        {
            // Initialize timeout timer (check every 2 seconds)
            _timeoutTimer = new System.Timers.Timer(2000);
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

            // Check 1: Max expected time exceeded
            if (elapsedTotal > _maxExpectedTimeSeconds)
            {
                HandleTimeout($"Timeout: Expected completion in {_maxExpectedTimeSeconds:F1}s, but elapsed {elapsedTotal:F1}s");
                return;
            }

            // Check 2: Silence detection (no packets for 10 seconds)
            if (elapsedSinceLastPacket > SilenceTimeoutSeconds)
            {
                HandleTimeout($"Timeout: No packets received for {elapsedSinceLastPacket:F1}s (signal lost)");
                return;
            }
        }

        private void HandleTimeout(string message)
        {
            Console.WriteLine($"[FileTransfer] {message}");
            _timeoutTimer.Stop();
            _isReceivingFile = false;

            TimeoutOccurred?.Invoke(this, message);
            DebugMessage?.Invoke(this, $"\n=== {message} ===\nReceived {_receivedChunkIds.Count}/{_expectedChunks} chunks\n");
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
                try
                {
                    // Parse Header
                    int nameLen = packet.Payload[0];
                    _currentFileName = Encoding.ASCII.GetString(packet.Payload, 1, nameLen);
                    _totalFileSize = BitConverter.ToInt32(packet.Payload, 1 + nameLen);

                    // Initialize Buffer
                    _fileBuffer = new byte[_totalFileSize];
                    _receivedChunkIds.Clear();
                    _isReceivingFile = true;
                    _expectedChunks = (int)Math.Ceiling((double)_totalFileSize / MaxChunkSize);

                    // Initialize timeout tracking
                    _receptionStartTime = DateTime.Now;
                    _lastPacketTime = DateTime.Now;
                    _maxExpectedTimeSeconds = _expectedChunks * AvgPacketTimeSeconds + 10; // Add 10s buffer
                    _timeoutTimer.Start();

                    string debugMsg = $"File: {_currentFileName}\nSize: {_totalFileSize / 1024.0:F1} KB\nExpected packets: {_expectedChunks}\nMax time: {_maxExpectedTimeSeconds:F1}s";
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
                // Update last packet time for silence detection
                _lastPacketTime = DateTime.Now;

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

                        if (_receivedChunkIds.Add(seqId)) // Only count if new
                        {
                            double progress = (double)_receivedChunkIds.Count / _expectedChunks;
                            ProgressChanged?.Invoke(this, progress);

                            string debugMsg = $"Chunk {seqId + 1}/{_expectedChunks} ({progress:P0})";
                            DebugMessage?.Invoke(this, debugMsg);
                            Console.WriteLine($"[FileTransfer] Received chunk {seqId}. Progress: {progress:P0}");

                            if (_receivedChunkIds.Count >= _expectedChunks)
                            {
                                FinishReception();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileTransfer] Error parsing chunk: {ex.Message}");
                }
            }
        }

        private void FinishReception()
        {
            Console.WriteLine($"[FileTransfer] FinishReception called. Chunks received: {_receivedChunkIds.Count}/{_expectedChunks}");
            _isReceivingFile = false;
            _timeoutTimer.Stop(); // Stop timeout timer on successful completion

            string receiveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedFiles");
            Console.WriteLine($"[FileTransfer] Target directory: {receiveDir}");

            if (!Directory.Exists(receiveDir))
            {
                Directory.CreateDirectory(receiveDir);
                Console.WriteLine($"[FileTransfer] Created directory: {receiveDir}");
            }

            string savePath = Path.Combine(receiveDir, _currentFileName);
            Console.WriteLine($"[FileTransfer] Initial save path: {savePath}");

            // Ensure unique name
            int counter = 1;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(_currentFileName);
            string ext = Path.GetExtension(_currentFileName);

            while (File.Exists(savePath))
            {
                savePath = Path.Combine(receiveDir, $"{fileNameWithoutExt}_{counter++}{ext}");
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

                    // Ensure unique name for decompressed file
                    counter = 1;
                    fileNameWithoutExt = Path.GetFileNameWithoutExtension(decompressedPath);
                    while (File.Exists(decompressedPath))
                    {
                        decompressedPath = Path.Combine(receiveDir, $"{fileNameWithoutExt}_{counter++}.png");
                    }

                    _imageCompressionService.DecompressImage(_fileBuffer, decompressedPath);

                    Console.WriteLine($"[FileTransfer] Decompressed to {decompressedPath}");
                    FileReceived?.Invoke(this, decompressedPath);

                    // Optional: Delete the compressed file
                    File.Delete(savePath);
                    Console.WriteLine($"[FileTransfer] Deleted temporary .cimg file");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileTransfer] Decompression failed: {ex.Message}");
                    FileReceived?.Invoke(this, savePath); // Return original if failed
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
    }
}
