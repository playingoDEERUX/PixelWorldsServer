using SevenZip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PixelWorldsServer
{
    class LZMAHelper
    {
        public static void CompressFileLZMA(string inFile, string outFile)
        {
            SevenZip.Compression.LZMA.Encoder coder = new SevenZip.Compression.LZMA.Encoder();
            FileStream input = new FileStream(inFile, FileMode.Open);
            FileStream output = new FileStream(outFile, FileMode.Create);

            // Write the encoder properties
            coder.WriteCoderProperties(output);

            // Write the decompressed file size.
            output.Write(BitConverter.GetBytes(input.Length), 0, 8);

            // Encode the file.
            coder.Code(input, output, input.Length, -1, null);
            output.Flush();
            output.Close();
        }

        public static byte[] CompressLZMA(byte[] compressed)
        {
            SevenZip.Compression.LZMA.Encoder encoder = new SevenZip.Compression.LZMA.Encoder();

            using (Stream input = new MemoryStream(compressed))
            {
                using (Stream output = new MemoryStream(512000)) // more optimized...
                {
                    encoder.SetCoderProperties(new CoderPropID[]
                    {
                        CoderPropID.DictionarySize
                    }, new object[]
                    {
                        (int)512000
                    });

                    encoder.WriteCoderProperties(output);
                    output.Write(BitConverter.GetBytes(input.Length), 0, 8);
                    encoder.Code(input, output, input.Length, -1L, null);

                    output.Flush();

                    return ((MemoryStream)output).ToArray();
                }
            }
        }

        public static byte[] DecompressLZMA(byte[] compressed)
        {
            SevenZip.Compression.LZMA.Decoder coder = new SevenZip.Compression.LZMA.Decoder();

            long fileLength = BitConverter.ToInt64(compressed, 5);

            using (Stream input = new MemoryStream(compressed))
            {
                using (Stream output = new MemoryStream((int)fileLength)) // more optimized...
                {
                    
                    byte[] properties = new byte[5];
                    input.Read(properties);

                    
                    byte[] sig = new byte[8]; // actually the length, again... :/
                    input.Read(sig);

                    coder.SetDecoderProperties(properties);
                    coder.Code(input, output, input.Length, fileLength, null);
                    output.Flush();

                    return ((MemoryStream)output).ToArray();
                }
            }
        }

        public static void DecompressFileLZMA(string inFile, string outFile)
        {
            SevenZip.Compression.LZMA.Decoder coder = new SevenZip.Compression.LZMA.Decoder();
            FileStream input = new FileStream(inFile, FileMode.Open);
            FileStream output = new FileStream(outFile, FileMode.Create);

            // Read the decoder properties
            byte[] properties = new byte[5];
            input.Read(properties, 0, 5);

            // Read in the decompress file size.
            byte[] fileLengthBytes = new byte[8];
            input.Read(fileLengthBytes, 0, 8);
            long fileLength = BitConverter.ToInt64(fileLengthBytes, 0);

            coder.SetDecoderProperties(properties);
            coder.Code(input, output, input.Length, fileLength, null);
            output.Flush();
            output.Close();
            input.Close();
        }
    }
}
