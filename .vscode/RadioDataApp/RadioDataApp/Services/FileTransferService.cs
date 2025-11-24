using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RadioDataApp.Modem;

namespace RadioDataApp.Services
{
    public class FileTransferService
    {
        private const int MaxChunkSize = 200; // Payload size limit (255 - overhead)

        // Receive State
        private bool _isReceivingFile;
        private string _currentFileName = "";
        private int _totalFileSize;
        private List<byte> _receivedFileBuffer = [];
        private int _expectedChunks;
        private int _receivedChunks;

        public event EventHandler<string>? FileReceived;
        public event EventHandler<double>? ProgressChanged;
        public event EventHandler<string>? DebugMessage;

        // Public state for debugging
        public int ReceivedChunks => _receivedChunks;
        public int ExpectedChunks => _expectedChunks;
        public string CurrentFileName => _currentFileName;
        public bool IsReceivingFile => _isReceivingFile;

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

                    _receivedFileBuffer.Clear();
                    _isReceivingFile = true;
                    _receivedChunks = 0;
                    _expectedChunks = (int)Math.Ceiling((double)_totalFileSize / MaxChunkSize);

                    string debugMsg = $"File: {_currentFileName}\nSize: {_totalFileSize / 1024.0:F1} KB\nExpected packets: {_expectedChunks}";
                    DebugMessage?.Invoke(this, debugMsg);

                    Console.WriteLine($"[FileTransfer] Starting receive: {_currentFileName} ({_totalFileSize} bytes, {_expectedChunks} chunks)");
                    ProgressChanged?.Invoke(this, 0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FileTransfer] Error parsing header: {ex.Message}");
                    _isReceivingFile = false;
                }
            }
            else if (packet.Type == CustomProtocol.PacketType.FileChunk && _isReceivingFile)
            {
                try
                {
                    // Parse Chunk
                    short seqId = BitConverter.ToInt16(packet.Payload, 0);
                    byte[] data = new byte[packet.Payload.Length - 2];
                    Array.Copy(packet.Payload, 2, data, 0, data.Length);

                    // Simple append (assuming in-order for now, robust implementation would use SeqID to place correctly)
                    _receivedFileBuffer.AddRange(data);
                    _receivedChunks++;

                    double progress = (double)_receivedFileBuffer.Count / _totalFileSize;
                    ProgressChanged?.Invoke(this, progress);

                    string debugMsg = $"Chunk {_receivedChunks}/{_expectedChunks} ({progress:P0})";
                    DebugMessage?.Invoke(this, debugMsg);

                    Console.WriteLine($"[FileTransfer] Received chunk {seqId}. Progress: {progress:P0}");

                    if (_receivedFileBuffer.Count >= _totalFileSize)
                    {
                        FinishReception();
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
            _isReceivingFile = false;
            string savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Received_" + _currentFileName);

            // Ensure unique name
            int counter = 1;
            while (File.Exists(savePath))
            {
                savePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"Received_{counter++}_{_currentFileName}");
            }

            File.WriteAllBytes(savePath, _receivedFileBuffer.ToArray());
            Console.WriteLine($"[FileTransfer] Saved to {savePath}");
            FileReceived?.Invoke(this, savePath);
            ProgressChanged?.Invoke(this, 1.0);
        }
    }
}
