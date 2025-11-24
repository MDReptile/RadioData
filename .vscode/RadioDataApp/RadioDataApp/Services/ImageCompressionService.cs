using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;

namespace RadioDataApp.Services
{
    public class ImageCompressionService
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        public static bool IsImageFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return Array.Exists(ImageExtensions, e => e == ext);
        }

        public static byte[] CompressImage(string filePath)
        {
            using var originalImage = new Bitmap(filePath);
            int width = originalImage.Width;
            int height = originalImage.Height;

            // Header: width (2 bytes) + height (2 bytes) + color depth marker (1 byte)
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write((ushort)width);
            bw.Write((ushort)height);
            bw.Write((byte)12); // 12-bit color depth marker

            // Compress pixels to 12-bit (4 bits per R, G, B channel)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = originalImage.GetPixel(x, y);

                    // Reduce from 8-bit to 4-bit per channel
                    byte r4 = (byte)(pixel.R >> 4);  // 255 → 15
                    byte g4 = (byte)(pixel.G >> 4);
                    byte b4 = (byte)(pixel.B >> 4);

                    // Pack two pixels into 3 bytes (24 bits = 2 × 12 bits)
                    if (x % 2 == 0)
                    {
                        // First pixel of pair
                        bw.Write((byte)((r4 << 4) | g4));  // RG
                        // Store B in temp for next pixel
                        if (x + 1 < width)
                        {
                            Color nextPixel = originalImage.GetPixel(x + 1, y);
                            byte r4_next = (byte)(nextPixel.R >> 4);
                            byte g4_next = (byte)(nextPixel.G >> 4);
                            byte b4_next = (byte)(nextPixel.B >> 4);

                            bw.Write((byte)((b4 << 4) | r4_next));  // BR
                            bw.Write((byte)((g4_next << 4) | b4_next));  // GB
                            x++; // Skip next pixel since we processed it
                        }
                        else
                        {
                            // Odd width - write last pixel
                            bw.Write(b4);
                        }
                    }
                }
            }

            return ms.ToArray();
        }

        public static void DecompressImage(byte[] compressedData, string outputPath)
        {
            using var ms = new MemoryStream(compressedData);
            using var br = new BinaryReader(ms);

            ushort width = br.ReadUInt16();
            ushort height = br.ReadUInt16();
            byte colorDepth = br.ReadByte();

            if (colorDepth != 12)
                throw new InvalidDataException("Unsupported color depth");

            using var image = new Bitmap(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x += 2)
                {
                    if (x + 1 < width)
                    {
                        // Read packed pair
                        byte rg = br.ReadByte();
                        byte br_byte = br.ReadByte();
                        byte gb = br.ReadByte();

                        byte r4 = (byte)(rg >> 4);
                        byte g4 = (byte)(rg & 0x0F);
                        byte b4 = (byte)(br_byte >> 4);

                        byte r4_next = (byte)(br_byte & 0x0F);
                        byte g4_next = (byte)(gb >> 4);
                        byte b4_next = (byte)(gb & 0x0F);

                        // Expand 4-bit to 8-bit
                        Color pixel1 = Color.FromArgb(r4 * 17, g4 * 17, b4 * 17);
                        Color pixel2 = Color.FromArgb(r4_next * 17, g4_next * 17, b4_next * 17);

                        image.SetPixel(x, y, pixel1);
                        image.SetPixel(x + 1, y, pixel2);
                    }
                    else
                    {
                        // Odd width - last pixel
                        byte rg = br.ReadByte();
                        byte b = br.ReadByte();

                        byte r4 = (byte)(rg >> 4);
                        byte g4 = (byte)(rg & 0x0F);

                        Color pixel = Color.FromArgb(r4 * 17, g4 * 17, b * 17);
                        image.SetPixel(x, y, pixel);
                    }
                }
            }

            image.Save(outputPath, ImageFormat.Png);
        }
    }
}
