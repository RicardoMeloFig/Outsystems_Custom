using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NetTools
{
    public static class Bits {
        public static byte[] Not(byte[] bytes) {
            byte[] result = new byte[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) {
                result[i] = (byte)~bytes[i];
            }
            return result;
        }

        public static byte[] And(byte[] A, byte[] B) {
            if (A.Length != B.Length) {
                throw new Exception("A.Length != B.Length");
            }
            byte[] result = new byte[A.Length];

            for (int i = 0; i < A.Length; i++) {
                result[i] = (byte)(A[i] & B[i]);
            }
            return result;
        }

        public static byte[] Or(byte[] A, byte[] B) {
            if (A.Length != B.Length) {
                throw new Exception("A.Length != B.Length");
            }
            byte[] result = new byte[A.Length];

            for (int i = 0; i < A.Length; i++) {
                result[i] = (byte)(A[i] | B[i]);
            }
            return result;
        }

        public static bool GE(byte[] A, byte[] B) {
            if (A.Length != B.Length) {
                throw new Exception("A.Length != B.Length");
            }
            int i = 0;
            while (i < A.Length && A[i] == B[i]) {
                i++;
            }
            //<st replaceBy="return i == A.length || Byte.toUnsignedInt(B[i]) >= Byte.toUnsignedInt(A[i]);
            //"/>
            return i == A.Length || B[i] >= A[i];
        }

        public static bool LE(byte[] A, byte[] B) {
            if (A.Length != B.Length) {
                throw new Exception("A.Length != B.Length");
            }
            int i = 0;
            while (i < A.Length && A[i] == B[i]) {
                i++;
            }
            //<st replaceBy="return i == A.length || !(Byte.toUnsignedInt(B[i]) > Byte.toUnsignedInt(A[i]));
            //"/>
            return i == A.Length || B[i] <= A[i];
        }

        public static byte[] GetBitMask(int sizeOfBuff, int bitLen) {
            var maskBytes = new byte[sizeOfBuff];
            var bytesLen = bitLen / 8;
            var bitsLen = bitLen % 8;
            for (var i = 0; i < bytesLen; i++) {
                maskBytes[i] = 0xff;
            }
            if (bitsLen > 0) maskBytes[bytesLen] = (byte)~Enumerable.Range(1, 8 - bitsLen).Select(n => 1 << n - 1).Aggregate((a, b) => a | b);
            return maskBytes;
        }

        /// <summary>
        /// Counts the number of leading 1's in a bitmask.
        /// Returns null if value is invalid as a bitmask.
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static int? GetBitMaskLength(byte[] bytes) {
            var bitLength = 0;
            var idx = 0;

            // find beginning 0xFF
            while (idx < bytes.Length && bytes[idx] == (byte)0xFF) {
                idx++;
            }

            bitLength = 8 * idx;

            if (idx < bytes.Length) {
                switch (bytes[idx]) {
                    case (byte)0xFE: bitLength += 7; break;
                    case (byte)0xFC: bitLength += 6; break;
                    case (byte)0xF8: bitLength += 5; break;
                    case (byte)0xF0: bitLength += 4; break;
                    case (byte)0xE0: bitLength += 3; break;
                    case (byte)0xC0: bitLength += 2; break;
                    case (byte)0x80: bitLength += 1; break;
                    case (byte)0x00: break;
                    default: // invalid bitmask
                        return null;
                }
                // remainder must be 0x00
                if (bytes.Skip(idx + 1).Any(x => x != (byte)0x00)) {
                    return null;
                }
            }
            return bitLength;
        }


        public static byte[] Increment(byte[] bytes) {
            IncrementAtIndex(bytes, bytes.Length - 1);
            return bytes;
        }

        private static void IncrementAtIndex(byte[] array, int index) {
            //<st replaceBy="byte MAX_VALUE = -0x01;
            //"/>
            byte MAX_VALUE = byte.MaxValue;
            if (array[index] == MAX_VALUE) {
                array[index] = 0;
                if (index > 0)
                    IncrementAtIndex(array, index - 1);
            } else {
                array[index]++;
            }
        }

    }
}
