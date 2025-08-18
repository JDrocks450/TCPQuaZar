using QuazarAPI.Util.Endian;
using System;
using System.IO;

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
}
