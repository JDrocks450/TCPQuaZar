using QuazarAPI.Util.Endian;
using System;
using System.IO;
using System.Text;

namespace QuazarAPI.Networking.Data
{
    public static class ExtendedBufferOperationsExtensions
    {
        public static void AllocateBody(this Stream _bodyBuffer, uint BodySize)
        {            
            _bodyBuffer.SetLength(BodySize);
            _bodyBuffer.Write(new byte[BodySize], 0, (int)BodySize);
            SetPosition(_bodyBuffer, 0);
        }
        public static void ReallocateBody(this Stream _bodyBuffer, uint NewSize)
        {
            _bodyBuffer.SetLength(NewSize);
        }
        public static void SetPosition(this Stream _bodyBuffer, int Position) => _bodyBuffer.Position = Position;
        public static void EmplaceBody(this Stream _bodyBuffer, params byte[] Bytes)
        {
            if ((Bytes.Length + _bodyBuffer.Position) > _bodyBuffer.Length)
                ReallocateBody(_bodyBuffer, (uint)(Bytes.Length + _bodyBuffer.Position));
            _bodyBuffer.Write(Bytes, 0, Bytes.Length);
        }
        public static void EmplaceBody(this Stream _bodyBuffer, uint DWORD, Endianness Endian = Endianness.BigEndian)
        {
            if (Endian == Endianness.BigEndian)
                EmplaceBody(_bodyBuffer, EndianBitConverter.Big.GetBytes(DWORD));
            else EmplaceBody(_bodyBuffer, EndianBitConverter.Little.GetBytes(DWORD));
        }
        public static void EmplaceBodyAt(this Stream _bodyBuffer, int Position, byte[] Buffer)
        {
            SetPosition(_bodyBuffer, Position);
            EmplaceBody(_bodyBuffer, Buffer);
        }
        public static void EmplaceBodyAt(this Stream _bodyBuffer, int Position, uint DWORD, Endianness Endian = Endianness.BigEndian)
        {
            SetPosition(_bodyBuffer, Position);
            EmplaceBody(_bodyBuffer, DWORD, Endian);
        }
        public static void EmplaceBodyAt(this Stream _bodyBuffer, int Position, byte Byte)
        {
            SetPosition(_bodyBuffer, Position);
            EmplaceBody(_bodyBuffer, Byte);
        }

        /// <summary>
        /// The byte, cast to an int
        /// </summary>
        /// <returns></returns>
        public static int ReadBodyByte(this Stream _bodyBuffer)
        {
            return (byte)_bodyBuffer.ReadByte();
        }
        public static byte[] ReadBodyByteArray(this Stream _bodyBuffer, int Length)
        {
            byte[] array = new byte[Length];
            _bodyBuffer.Read(array, 0, Length);
            return array;
        }
        public static ushort ReadBodyUshort(this Stream _bodyBuffer, Endianness Endian = Endianness.BigEndian)
        {
            if (Endian == Endianness.BigEndian)
                return EndianBitConverter.Big.ToUInt16(ReadBodyByteArray(_bodyBuffer, 2), 0);
            else return EndianBitConverter.Little.ToUInt16(ReadBodyByteArray(_bodyBuffer, 2), 0);
        }
        public static uint ReadBodyDword(this Stream _bodyBuffer, Endianness Endian = Endianness.BigEndian)
        {
            if (Endian == Endianness.BigEndian)
                return EndianBitConverter.Big.ToUInt32(ReadBodyByteArray(_bodyBuffer, 4), 0);
            else return EndianBitConverter.Little.ToUInt32(ReadBodyByteArray(_bodyBuffer, 4), 0);
        }
        public static string ReadBodyNullTerminatedString(this Stream _bodyBuffer, int MaxSize, char NullTerminatorChar = '\0')
        {
            long BodyLength = _bodyBuffer.Length;
            long BodyPosition = _bodyBuffer.Position;
            int readLen = Math.Min(MaxSize, (int)(BodyLength - BodyPosition));
            string myStr = "";
            for (int i = 0; i < readLen; i += sizeof(char))
            {                
                int response = _bodyBuffer.ReadByte();
                if (response == -1) break;
                char character = (char)response;
                if (character == NullTerminatorChar) break;
                myStr += character;
            }
            return myStr;
            byte[] str = ReadBodyByteArray(_bodyBuffer, readLen);
            //string myStr = "";
            myStr = Encoding.UTF8.GetString(str);
            if (myStr.Contains(NullTerminatorChar))
                myStr = myStr.Remove(myStr.IndexOf(NullTerminatorChar));
            return myStr;
        }
        public static string ReadBodyNullTerminatedString(this Stream _bodyBuffer, int Offset, int MaxSize)
        {
            SetPosition(_bodyBuffer, Offset);
            return ReadBodyNullTerminatedString(_bodyBuffer, MaxSize);
        }

        /// <summary>
        /// Advances the <see cref="BodyPosition"/> by the <paramref name="amount"/> given.
        /// </summary>
        /// <param name="amount"></param>
        public static void Advance(this Stream _bodyBuffer, int amount = 1) => SetPosition(_bodyBuffer, (int)_bodyBuffer.Position + amount);
        /// <summary>
        /// Reads all data until the end of the buffer
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public static byte[] ReadToEnd(this Stream _bodyBuffer) => ReadBodyByteArray(_bodyBuffer, (int)(_bodyBuffer.Length - _bodyBuffer.Position));
    }
}
