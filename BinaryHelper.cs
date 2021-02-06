using System;
using System.Collections.Generic;
using System.Text;

namespace PixelWorldsServer
{
    class BinaryHelper
    {
        public static void SetBit(ref int integer, byte nPos) => integer |= 1 << nPos;
        public static bool GetBit(ref int integer, byte nPos)
        {
            return (integer & (1 << nPos)) != 0;
        }

        public static string GetBitTF(ref int integer, byte nPos) // True or False
        {
            return ((integer & (1 << nPos)) != 0) ? "true" : "false";
        }
        public static string GetBitTF(ref short integer, byte nPos) // True or False
        {
            return ((integer & (1 << nPos)) != 0) ? "true" : "false";
        }

        public static string GetBitTF(ref byte integer, byte nPos) // True or False
        {
            return ((integer & (1 << nPos)) != 0) ? "true" : "false";
        }

        public static void SetBit(ref short integer, byte nPos) => integer |= (short)(1 << nPos);
        public static bool GetBit(ref short integer, byte nPos)
        {
            return (integer & (1 << nPos)) != 0;
        }
        public static void SetBit(ref byte integer, byte nPos) => integer |= (byte)(1 << nPos);
        public static bool GetBit(ref byte integer, byte nPos)
        {
            return (integer & (1 << nPos)) != 0;
        }
    }
}
