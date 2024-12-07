using MiscUtil.Conversion;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public static string ReadBodyNullTerminatedString(this Stream _bodyBuffer, int MaxSize)
        {
            long BodyLength = _bodyBuffer.Length;
            long BodyPosition = _bodyBuffer.Position;
            int readLen = Math.Min(MaxSize, (int)(BodyLength - BodyPosition));
            byte[] str = ReadBodyByteArray(_bodyBuffer, readLen);
            string myStr = "";
            myStr = Encoding.UTF8.GetString(str);
            if (myStr.Contains('\0'))
                myStr = myStr.Remove(myStr.IndexOf('\0'));
            return myStr;
            for (int i = 0; i < readLen; i += 2)
            {
                short myChar = 0;
                if (str[i] == '\0') break;
                myStr += (char)str[i];
            }
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

    public abstract class ExtendedBufferOperations : IDisposable
    {
        public virtual uint BodyLength => (uint)_bodyBuffer.Length;
        protected MemoryStream _bodyBuffer = new MemoryStream();
        private bool disposedValue;
        public byte[] Body
        {
            get => _bodyBuffer.ToArray();
            set
            {
                AllocateBody((uint)value.Length);
                EmplaceBody(value);
            }
        }
        public long BodyPosition => _bodyBuffer.Position;
        public bool IsBodyEOF => _bodyBuffer.Position == BodyLength;

        public void AllocateBody(uint BodySize) => _bodyBuffer.AllocateBody(BodySize);
        public void ReallocateBody(uint NewSize) => _bodyBuffer.ReallocateBody(NewSize);
        public void SetPosition(int Position) => _bodyBuffer.Position = Position;
        public void EmplaceBody(params byte[] Bytes) => _bodyBuffer.EmplaceBody(Bytes);
        public void EmplaceBody(uint DWORD, Endianness Endian = Endianness.BigEndian) => _bodyBuffer.EmplaceBody(DWORD, Endian);
        public void EmplaceBodyAt(int Position, byte[] Buffer) => _bodyBuffer.EmplaceBodyAt(Position, Buffer);
        public void EmplaceBodyAt(int Position, uint DWORD, Endianness Endian = Endianness.BigEndian) => _bodyBuffer.EmplaceBodyAt(Position, DWORD, Endian);
        public void EmplaceBodyAt(int Position, byte Byte) => _bodyBuffer.EmplaceBodyAt(Position, Byte);

        /// <summary>
        /// The byte, cast to an int
        /// </summary>
        /// <returns></returns>
        public int ReadBodyByte() => _bodyBuffer.ReadBodyByte();
        public byte[] ReadBodyByteArray(int Length) => _bodyBuffer.ReadBodyByteArray(Length);
        public ushort ReadBodyUshort(Endianness Endian = Endianness.BigEndian) => _bodyBuffer.ReadBodyUshort(Endian);
        public uint ReadBodyDword(Endianness Endian = Endianness.BigEndian) => _bodyBuffer.ReadBodyDword(Endian);
        public string ReadBodyNullTerminatedString(int MaxSize) => _bodyBuffer.ReadBodyNullTerminatedString(MaxSize);
        public string ReadBodyNullTerminatedString(int Offset, int MaxSize) => _bodyBuffer.ReadBodyNullTerminatedString(Offset, MaxSize);
        
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _bodyBuffer.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }
        /// <summary>
        /// Advances the <see cref="BodyPosition"/> by the <paramref name="amount"/> given.
        /// </summary>
        /// <param name="amount"></param>
        public void Advance(int amount = 1) => SetPosition((int)BodyPosition + amount);
        /// <summary>
        /// Reads all data until the end of the buffer
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public byte[] ReadToEnd() => ReadBodyByteArray((int)(BodyLength - BodyPosition));

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~TPWPacket()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public abstract class PacketBase : ExtendedBufferOperations
    {
        /// <summary>
        /// Get the size of the header of this <see cref="PacketBase"/>, in bytes
        /// </summary>
        /// <returns></returns>
        public abstract uint GetHeaderSize();

        public DateTime Sent { get; set; }
        public DateTime Received { get; set; }

        internal List<PacketBase> splitPackets = new List<PacketBase>();        
        public bool HasChildPackets => splitPackets.Count > 0;
        public int ChildPacketAmount => splitPackets.Count;
        public string ReceivedTime => Received == default ? "Not Received" : Received.ToString();
        public string SentTime => Sent == default ? "Not Sent" : Sent.ToString();
        public static IEnumerable<T> ParseAll<T>(ref byte[] Data) where T : PacketBase, new() => new T().ParseAllPackets<T>(ref Data);
        public static T Parse<T>(byte[] bytes, out int endIndex) where T : PacketBase, new() => new T().ParsePacket<T>(bytes, out endIndex);
        public abstract T ParsePacket<T>(byte[] bytes, out int endIndex) where T : PacketBase, new();
        public abstract IEnumerable<T> ParseAllPackets<T>(ref byte[] Data) where T : PacketBase, new();
        /// <summary>
        /// Try to read a packet header from the buffer and will output how much data to read from the network buffer
        /// </summary>
        /// <param name="Buffer"></param>
        /// <param name="ReadSize"></param>
        /// <returns></returns>
        public abstract bool TryGetHeaderData(in Byte[] Buffer, out uint ReadSize);     

        /// <summary>
        /// Adds a packet to this one as a child. 
        /// <para>This is used to allow packets to be split by the API without needing to use custom types.
        /// The API will automatically detect and send these child packets with the primary packet.
        /// </para>
        /// </summary>
        /// <param name="Packets"></param>
        public void AppendChildPackets(params PacketBase[] Packets)
        {
            splitPackets.AddRange(Packets.Where(x => x != null));
            if (splitPackets.Remove(this))
            {
                QConsole.WriteLine("TPWPacket API", "The primary packet was found as a child packet of itself." +
                    " However this happened, don't let it happen again.");
            }
        }
        public uint GetChildPacketBodyLength()
        {
            uint bodyLen = 0;
            foreach (var packet in splitPackets)
                bodyLen += packet.BodyLength;
            return bodyLen;
        }
        public void MergeBody(PacketBase packet, int index)
        {
            MergeBody(packet.Body, index);
        }
        public void MergeBody(byte[] source, int index = 0)
        {
            byte[] buffer = Body;
            int inputLeng = buffer.Length;
            Array.Resize(ref buffer, Body.Length + (source.Length - index));
            source.Skip(index).ToArray().CopyTo(buffer, inputLeng);
            Body = buffer;
        }

        public int Write(in Stream Buffer)
        {
            byte[] buffer = GetBytes();
            Buffer.Write(buffer,0,buffer.Length);
            return buffer.Length;
        }
        
        public virtual byte[] GetBytes() => Body;
    }
}
