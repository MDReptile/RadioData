using RadioDataApp.Modem;
using System.Text;
using Xunit;

namespace RadioData.Tests
{
    public class ModemTests
    {
        [Fact]
        public void Modulate_ShouldGenerateAudioData()
        {
            // Arrange
            var modem = new AfskModem();
            string text = "Test";
            byte[] packet = CustomProtocol.Encode(text);

            // Act
            byte[] audio = modem.Modulate(packet);

            // Assert
            Assert.NotNull(audio);
            Assert.True(audio.Length > 0);
            // Rough check: 4 chars + overhead -> some bytes -> modulated at 44.1kHz
            // It should be substantial
            Assert.True(audio.Length > 1000);
        }

        [Fact]
        public void Loopback_Text_ShouldSucceed()
        {
            // Arrange
            var modem = new AfskModem();
            string originalText = "Hello World";
            byte[] packet = CustomProtocol.Encode(originalText);
            byte[] audio = modem.Modulate(packet);

            // Act
            var decodedPacket = modem.Demodulate(audio);

            // Assert
            Assert.NotNull(decodedPacket);
            Assert.Equal(CustomProtocol.PacketType.Text, decodedPacket.Type);
            string decodedText = Encoding.ASCII.GetString(decodedPacket.Payload);
            Assert.Equal(originalText, decodedText);
        }

        [Theory]
        [InlineData("Key1")]
        [InlineData("SuperSecretKey")]
        [InlineData("1234567890")]
        public void Loopback_WithDifferentKeys_ShouldSucceed(string key)
        {
            // Arrange
            var modem = new AfskModem();
            string originalText = "Encrypted Message";
            
            // Save old key to restore later (though tests run in parallel, so this might be flaky if static state is shared)
            // CustomProtocol.EncryptionKey is static! This is a design flaw for parallel tests.
            // We should lock or run sequentially. XUnit runs classes in parallel but methods within a class sequentially by default?
            // Actually XUnit 2.x runs collections in parallel. By default each class is a collection.
            // Since we only have one test class, it should be fine.
            
            string oldKey = CustomProtocol.EncryptionKey;
            try
            {
                CustomProtocol.EncryptionKey = key;
                byte[] packet = CustomProtocol.Encode(originalText);
                byte[] audio = modem.Modulate(packet);

                // Act
                var decodedPacket = modem.Demodulate(audio);

                // Assert
                Assert.NotNull(decodedPacket);
                string decodedText = Encoding.ASCII.GetString(decodedPacket.Payload);
                Assert.Equal(originalText, decodedText);
            }
            finally
            {
                CustomProtocol.EncryptionKey = oldKey;
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("A")]
        [InlineData("This is a longer message to test if the modem can handle more data without losing sync or bits.")]
        [InlineData("!@#$%^&*()_+{}|:\"<>?")]
        public void Loopback_WithDifferentPayloads_ShouldSucceed(string payload)
        {
            // Arrange
            var modem = new AfskModem();
            byte[] packet = CustomProtocol.Encode(payload);
            byte[] audio = modem.Modulate(packet);

            // Act
            var decodedPacket = modem.Demodulate(audio);

            // Assert
            Assert.NotNull(decodedPacket);
            string decodedText = Encoding.ASCII.GetString(decodedPacket.Payload);
            Assert.Equal(payload, decodedText);
        }

        [Fact]
        public void Demodulate_BelowSquelch_ShouldReturnNull()
        {
            // Arrange
            var modem = new AfskModem();
            modem.SquelchThreshold = 0.5f; // Set high threshold
            
            // Generate silence or very low noise
            byte[] silence = new byte[1000]; // Zeros

            // Act
            var result = modem.Demodulate(silence);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Demodulate_WithWrongKey_ShouldFail()
        {
            // Arrange
            var modem = new AfskModem();
            string originalText = "Secret Data";
            string correctKey = "CorrectKey";
            string wrongKey = "WrongKey";

            string oldKey = CustomProtocol.EncryptionKey;
            try
            {
                // Encode with correct key
                CustomProtocol.EncryptionKey = correctKey;
                byte[] packet = CustomProtocol.Encode(originalText);
                byte[] audio = modem.Modulate(packet);

                // Decode with wrong key
                CustomProtocol.EncryptionKey = wrongKey;
                var decodedPacket = modem.Demodulate(audio);

                // Assert
                // It might return null (checksum fail) OR return garbage
                // The current implementation checksums the ENCRYPTED payload.
                // So if we change the key *after* encoding, the checksum (which is based on encrypted data) is still valid for the packet structure.
                // But when we decrypt with the wrong key, we get garbage.
                
                if (decodedPacket != null)
                {
                    string decodedText = Encoding.ASCII.GetString(decodedPacket.Payload);
                    Assert.NotEqual(originalText, decodedText);
                }
                // If it returns null, that's also acceptable (maybe some other check failed)
            }
            finally
            {
                CustomProtocol.EncryptionKey = oldKey;
            }
        }
    }
}
