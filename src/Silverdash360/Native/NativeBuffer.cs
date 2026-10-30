using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Silverdash360.Native
{
    /// <summary>
    /// A pinned block of memory for passing structures to the console's
    /// kernel and system library. The process is big-endian with 32-bit
    /// pointers, so fields are read and written big-endian and addresses are
    /// plain uints. Keep a buffer only for the call that needs it: pinned
    /// memory stops the garbage collector from compacting around it.
    /// </summary>
    public sealed class NativeBuffer : IDisposable
    {
        private GCHandle handle;

        public NativeBuffer(int size)
        {
            Bytes = new byte[Math.Max(size, 4)];
            handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
            Address = (uint)handle.AddrOfPinnedObject().ToInt32();
        }

        public NativeBuffer(byte[] contents) : this(contents.Length)
        {
            Buffer.BlockCopy(contents, 0, Bytes, 0, contents.Length);
        }

        public byte[] Bytes { get; private set; }

        /// <summary>The guest address of the first byte.</summary>
        public uint Address { get; private set; }

        public int Length { get { return Bytes.Length; } }

        /// <summary>The address of a byte inside the buffer.</summary>
        public uint this[int offset] { get { return Address + (uint)offset; } }

        /// <summary>Whether a pointer the native side wrote points into the buffer.</summary>
        public bool Contains(uint pointer)
        {
            return pointer >= Address && pointer < Address + (uint)Bytes.Length;
        }

        /// <summary>The offset of a pointer into the buffer.</summary>
        public int OffsetOf(uint pointer)
        {
            return (int)(pointer - Address);
        }

        public byte U8(int offset) { return Bytes[offset]; }
        public ushort U16(int offset) { return BigEndian.U16(Bytes, offset); }
        public uint U32(int offset) { return BigEndian.U32(Bytes, offset); }
        public ulong U64(int offset) { return BigEndian.U64(Bytes, offset); }
        public void SetU16(int offset, ushort value) { BigEndian.SetU16(Bytes, offset, value); }
        public void SetU32(int offset, uint value) { BigEndian.SetU32(Bytes, offset, value); }
        public void SetU64(int offset, ulong value) { BigEndian.SetU64(Bytes, offset, value); }

        public string Utf16(int offset, int maxChars)
        {
            return BigEndian.Utf16(Bytes, offset, maxChars);
        }

        public string Ansi(int offset, int maxBytes)
        {
            return BigEndian.Ansi(Bytes, offset, maxBytes);
        }

        /// <summary>A UTF-16 string a pointer in the buffer points at, or null.</summary>
        public string Utf16At(uint pointer)
        {
            return Contains(pointer) ? Utf16(OffsetOf(pointer), (Bytes.Length - OffsetOf(pointer)) / 2) : null;
        }

        public byte[] Slice(int offset, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(Bytes, offset, result, 0, length);
            return result;
        }

        public void Dispose()
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
    }

    /// <summary>Big-endian reads and writes on byte arrays.</summary>
    public static class BigEndian
    {
        public static ushort U16(byte[] b, int o)
        {
            return (ushort)(b[o] << 8 | b[o + 1]);
        }

        public static uint U32(byte[] b, int o)
        {
            return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        }

        public static ulong U64(byte[] b, int o)
        {
            return (ulong)U32(b, o) << 32 | U32(b, o + 4);
        }

        public static void SetU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)(v >> 8);
            b[o + 1] = (byte)v;
        }

        public static void SetU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }

        public static void SetU64(byte[] b, int o, ulong v)
        {
            SetU32(b, o, (uint)(v >> 32));
            SetU32(b, o + 4, (uint)v);
        }

        /// <summary>A NUL-terminated UTF-16BE string of at most maxChars.</summary>
        public static string Utf16(byte[] b, int o, int maxChars)
        {
            StringBuilder s = new StringBuilder();
            for (int i = 0; i < maxChars && o + i * 2 + 1 < b.Length; i++)
            {
                char c = (char)U16(b, o + i * 2);
                if (c == '\0')
                {
                    break;
                }
                s.Append(c);
            }
            return s.ToString();
        }

        /// <summary>A NUL-terminated single-byte string of at most maxBytes.</summary>
        public static string Ansi(byte[] b, int o, int maxBytes)
        {
            int n = 0;
            while (n < maxBytes && o + n < b.Length && b[o + n] != 0)
            {
                n++;
            }
            return Encoding.UTF8.GetString(b, o, n);
        }
    }
}
