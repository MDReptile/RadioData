using RadioDataApp.Modem;
using System;
using System.Windows;
using System.Windows.Threading;

namespace RadioDataApp.ViewModels
{
    public partial class MainViewModel
    {
        private DispatcherTimer? _silenceTimer;

        private void OnAudioDataReceived(object? sender, byte[] audioData)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                long sum = 0;
                for (int i = 0; i < audioData.Length; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    sum += sample * sample;
                }
                double rms = Math.Sqrt((double)sum / (audioData.Length / 2));
                InputVolume = Math.Min(1.0, rms / 10000.0);

                int zeroCrossings = 0;
                short prevSample = 0;
                for (int i = 0; i < audioData.Length; i += 2)
                {
                    short sample = BitConverter.ToInt16(audioData, i);
                    if ((prevSample < 0 && sample >= 0) || (prevSample >= 0 && sample < 0))
                        zeroCrossings++;
                    prevSample = sample;
                }
                double duration = audioData.Length / 2.0 / 44100.0;
                double freq = (zeroCrossings / 2.0) / duration;

                if (InputVolume > 0.05 && freq >= 500 && freq <= 3000)
                    InputFrequency = freq;
                else if (InputVolume == 0)
                    InputFrequency = 0;

                float normalizedRms = (float)(rms / 32768.0);
                if (normalizedRms >= _modem.SquelchThreshold)
                {
                    if (!IsReceiving && !IsTransferring)
                    {
                        IsReceiving = true;
                    }

                    if (!IsTransferring)
                    {
                        if (_silenceTimer == null)
                        {
                            _silenceTimer = new DispatcherTimer();
                            _silenceTimer.Interval = TimeSpan.FromSeconds(2);
                            _silenceTimer.Tick += (s, args) =>
                            {
                                IsReceiving = false;
                                _silenceTimer.Stop();
                            };
                        }

                        _silenceTimer.Stop();
                        _silenceTimer.Start();
                    }
                    else
                    {
                        _fileTransferService.NotifyAudioReceived();
                    }
                }

                var packet = _modem.Demodulate(audioData);
                if (packet != null)
                {
                    switch (packet.Type)
                    {
                        case CustomProtocol.PacketType.Text:
                            string receivedMessage = System.Text.Encoding.ASCII.GetString(packet.Payload);
                            string senderName = packet.SenderName ?? "Remote";
                            string timestamp = DateTime.Now.ToString("yyyy/MM/dd 'at' h:mm tt");
                            ChatLog += $"<< [{senderName}] {receivedMessage} : Received {timestamp}\n";
                            break;
                        case CustomProtocol.PacketType.FileHeader:
                            DebugLog += "\n=== RECEIVING FILE ===\n";
                            DebugLog += "<< Incoming file transfer\n";
                            IsTransferring = true;
                            _silenceTimer?.Stop();
                            _fileTransferService.HandlePacket(packet);
                            break;
                        case CustomProtocol.PacketType.FileChunk:
                            _fileTransferService.HandlePacket(packet);
                            break;
                    }
                }
            });
        }
    }
}
