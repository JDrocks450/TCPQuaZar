using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuazarAPI.Networking.Data
{

    public abstract class PacketBase : ExtendedBufferOperations
    {
        /// <summary>
        /// Get the size of the header of this <see cref="PacketBase"/>, in bytes
        /// </summary>
        /// <returns></returns>
        public abstract uint GetHeaderSize();

        public DateTime Sent { get; set; }
        public DateTime Received { get; set; }

        /// <summary>
        /// The ID of the Quazar connection that sent this packet
        /// </summary>
        public uint ConnectionID { get; set; }
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
