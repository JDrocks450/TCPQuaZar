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

        public void AllocateBody(uint BodySize)
        {
            if (_bodyBuffer != null)
                _bodyBuffer.Dispose();
            _bodyBuffer = new MemoryStream();
            _bodyBuffer.SetLength(BodySize);
            SetPosition(0);
        }
        public void ReallocateBody(uint NewSize)
        {
            _bodyBuffer.SetLength(NewSize);
        }
        public void SetPosition(int Position) => _bodyBuffer.Position = Position;
        public void EmplaceBody(params byte[] Bytes)
        {
            if ((Bytes.Length + _bodyBuffer.Position) > _bodyBuffer.Length)
                ReallocateBody((uint)(Bytes.Length + _bodyBuffer.Position));
            _bodyBuffer.Write(Bytes, 0, Bytes.Length);
        }
        public void EmplaceBody(uint DWORD, Endianness Endian = Endianness.BigEndian)
        {
            if (Endian == Endianness.BigEndian)
                EmplaceBody(EndianBitConverter.Big.GetBytes(DWORD));
            else EmplaceBody(EndianBitConverter.Little.GetBytes(DWORD));
        }
        public void EmplaceBodyAt(int Position, byte[] Buffer)
        {
            SetPosition(Position);
            EmplaceBody(Buffer);
        }
        public void EmplaceBodyAt(int Position, uint DWORD, Endianness Endian = Endianness.BigEndian)
        {
            SetPosition(Position);
            EmplaceBody(DWORD, Endian);
        }
        public void EmplaceBodyAt(int Position, byte Byte)
        {
            SetPosition(Position);
            EmplaceBody(Byte);
        }

        /// <summary>
        /// The byte, cast to an int
        /// </summary>
        /// <returns></returns>
        public int ReadBodyByte()
        {
            return (byte)_bodyBuffer.ReadByte();
        }
        public byte[] ReadBodyByteArray(int Length)
        {
            byte[] array = new byte[Length];
            _bodyBuffer.Read(array, 0, Length);
            return array;
        }
        public ushort ReadBodyUshort(Endianness Endian = Endianness.BigEndian)
        {
            if (Endian == Endianness.BigEndian)
                return EndianBitConverter.Big.ToUInt16(ReadBodyByteArray(2), 0);
            else return EndianBitConverter.Little.ToUInt16(ReadBodyByteArray(2), 0);
        }
        public uint ReadBodyDword(Endianness Endian = Endianness.BigEndian)
        {
            if (Endian == Endianness.BigEndian)
                return EndianBitConverter.Big.ToUInt32(ReadBodyByteArray(4), 0);
            else return EndianBitConverter.Little.ToUInt32(ReadBodyByteArray(4), 0);
        }
        public string ReadBodyNullTerminatedString(int MaxSize)
        {
            int readLen = Math.Min(MaxSize, (int)(BodyLength - BodyPosition));
            byte[] str = ReadBodyByteArray(readLen);
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
        public string ReadBodyNullTerminatedString(int Offset, int MaxSize)
        {
            SetPosition(Offset);
            return ReadBodyNullTerminatedString(MaxSize);
        }
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

        public void Advance(int amount = 1) => SetPosition((int)BodyPosition + amount);

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
