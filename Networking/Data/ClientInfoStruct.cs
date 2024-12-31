using System;

namespace QuazarAPI.Networking.Data
{
    [Serializable]
    /// <summary>
    /// The body of a <see cref="Commands.CLIENTINFO"/> command
    /// </summary>
    public class ClientInfo
    {
        public DateTime ConnectTime
        {
            get; set;
        }
        public string Name
        {
            get; set;
        } = "User";
        public uint ID
        {
            get; set;
        }
        /// <summary>
        /// A number that can be used to identify a client of a server by a known-role. 
        /// </summary>
        public int Me { get; set; }

        public ClientInfo()
        {

        }

        public ClientInfo(int me, string name)
        {
            Name = name;
            Me = me;
        }

        public override string ToString()
        {
            return $"ClientInfo [{ID}, {Name}]";
        }
    }
}
