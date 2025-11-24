using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RadioDataApp.Services
{
    public class ImageCompressionService
    {
        // Header: [Width(2)][Height(2)]
        // Data: 3 bytes per 2 pixels (12 bits per pixel)
        // 12 bits = 4R + 4G + 4B

        public static bool IsImageFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
        }

        public byte[] CompressImage(string filePath)
        {
            // 1. Load Image
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            // 2. Resize if too large (optional, but good for radio)
            // For now, keep original size but just compress colors
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            // 3. Get Pixels
            int stride = width * 4; // Assuming BGRA32
            byte[] rawPixels = new byte[height * stride];
            bitmap.CopyPixels(rawPixels, stride, 0);

            // 4. Compress
            // Output size: Header (4) + (Pixels / 2 * 3)
            int pixelCount = width * height;
            int compressedDataSize = (int)Math.Ceiling(pixelCount / 2.0) * 3;
            byte[] compressed = new byte[4 + compressedDataSize];

            // Write Header
            BitConverter.GetBytes((short)width).CopyTo(compressed, 0);
            BitConverter.GetBytes((short)height).CopyTo(compressed, 2);

            int outIdx = 4;
            for (int i = 0; i < pixelCount; i += 2)
            {
                // Pixel 1
                int p1Idx = i * 4;
                byte b1 = rawPixels[p1Idx];
                byte g1 = rawPixels[p1Idx + 1];
                byte r1 = rawPixels[p1Idx + 2];
                // a1 is ignored

                // Reduce to 4 bits (0-15)
                byte r1_4 = (byte)(r1 >> 4);
                byte g1_4 = (byte)(g1 >> 4);
                byte b1_4 = (byte)(b1 >> 4);

                // Pixel 2
                byte r2_4 = 0, g2_4 = 0, b2_4 = 0;
                if (i + 1 < pixelCount)
                {
                    int p2Idx = (i + 1) * 4;
                    byte b2 = rawPixels[p2Idx];
                    byte g2 = rawPixels[p2Idx + 1];
                    byte r2 = rawPixels[p2Idx + 2];

                    r2_4 = (byte)(r2 >> 4);
                    g2_4 = (byte)(g2 >> 4);
                    b2_4 = (byte)(b2 >> 4);
                }

                // Pack 2 pixels (24 bits) into 3 bytes
                // P1: RRRR GGGG BBBB
                // P2: rrrr gggg bbbb
                // Byte 0: RRRR GGGG
                // Byte 1: BBBB rrrr
                // Byte 2: gggg bbbb

                compressed[outIdx++] = (byte)((r1_4 << 4) | g1_4);
                compressed[outIdx++] = (byte)((b1_4 << 4) | r2_4);
                compressed[outIdx++] = (byte)((g2_4 << 4) | b2_4);
            }

            return compressed;
        }

        public void DecompressImage(byte[] compressedData, string outputPath)
        {
            if (compressedData.Length < 4) return;

            // 1. Read Header
            short width = BitConverter.ToInt16(compressedData, 0);
            short height = BitConverter.ToInt16(compressedData, 2);

            // 2. Prepare Output Buffer (BGRA32)
            int stride = width * 4;
            byte[] rawPixels = new byte[height * stride];

            int inIdx = 4;
            int pixelCount = width * height;

            for (int i = 0; i < pixelCount; i += 2)
            {
                if (inIdx + 2 >= compressedData.Length) break;

                // Read 3 bytes
                byte b0 = compressedData[inIdx++];
                byte b1 = compressedData[inIdx++];
                byte b2 = compressedData[inIdx++];

                // Unpack
                // Byte 0: RRRR GGGG
                // Byte 1: BBBB rrrr
                // Byte 2: gggg bbbb

                byte r1_4 = (byte)((b0 >> 4) & 0xF);
                byte g1_4 = (byte)(b0 & 0xF);
                byte b1_4 = (byte)((b1 >> 4) & 0xF);

                byte r2_4 = (byte)(b1 & 0xF);
                byte g2_4 = (byte)((b2 >> 4) & 0xF);
                byte b2_4 = (byte)(b2 & 0xF);

                // Scale up to 8 bits (0-255)
                // x * 17 maps 0-15 to 0-255 (e.g. 15*17=255)
                byte r1_8 = (byte)(r1_4 * 17);
                byte g1_8 = (byte)(g1_4 * 17);
                byte b1_8 = (byte)(b1_4 * 17);

                byte r2_8 = (byte)(r2_4 * 17);
                byte g2_8 = (byte)(g2_4 * 17);
                byte b2_8 = (byte)(b2_4 * 17);

                // Write Pixel 1
                int p1Idx = i * 4;
                rawPixels[p1Idx] = b1_8;
                rawPixels[p1Idx + 1] = g1_8;
                rawPixels[p1Idx + 2] = r1_8;
                rawPixels[p1Idx + 3] = 255; // Alpha

                // Write Pixel 2
                if (i + 1 < pixelCount)
                {
                    int p2Idx = (i + 1) * 4;
                    rawPixels[p2Idx] = b2_8;
                    rawPixels[p2Idx + 1] = g2_8;
                    rawPixels[p2Idx + 2] = r2_8;
                    rawPixels[p2Idx + 3] = 255; // Alpha
                }
            }

            // 3. Save to File
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rawPixels, stride);

            using (var stream = new FileStream(outputPath, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }
        }
    }
}
