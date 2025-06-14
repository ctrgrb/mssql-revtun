using System.Net.Sockets;

namespace RevTun
{    public class ProxyConnection
    {
        public uint Id { get; set; }
        public TcpClient ProxyClient { get; set; }
        public NetworkStream ProxyStream { get; set; }
        public string TargetHost { get; set; }
        public int TargetPort { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool ResponseSent { get; set; }
        public bool IsSocks5 { get; set; }
        
        public ProxyConnection(uint id, TcpClient client)
        {
            Id = id;
            ProxyClient = client;
            ProxyStream = client.GetStream();
            CreatedAt = DateTime.Now;
            IsActive = false; // Will be set to true when tunnel is established
            ResponseSent = false;
            IsSocks5 = false;
            TargetHost = "";
            TargetPort = 0;
        }
    }
      public class MssqlClientHandler
    {
        public TcpClient TcpClient { get; set; }
        public Stream Stream { get; set; }
        public string ClientEndpoint { get; set; }
        public DateTime ConnectedAt { get; set; }
        public bool IsAuthenticated { get; set; }
        public bool IsEncrypted { get; set; }
        
        public MssqlClientHandler(TcpClient client)
        {
            TcpClient = client;
            Stream = client.GetStream();
            ClientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            ConnectedAt = DateTime.Now;
            IsAuthenticated = false;
            IsEncrypted = false;
        }
    }
    
    // Custom TDS message types for tunnel communication
    public static class TunnelProtocol
    {
        public const byte TUNNEL_DATA = 0xF0;           // Custom: Tunnel data packet
        public const byte TUNNEL_CONNECT = 0xF1;        // Custom: Connect to target
        public const byte TUNNEL_CONNECT_ACK = 0xF2;    // Custom: Connection acknowledgment
        public const byte TUNNEL_DISCONNECT = 0xF3;     // Custom: Disconnect from target
        public const byte TUNNEL_ERROR = 0xF4;          // Custom: Error response
        
        public static byte[] CreateTunnelConnectPacket(uint connectionId, string host, int port)
        {
            var payload = new List<byte>();
            
            // Connection ID (4 bytes)
            payload.AddRange(BitConverter.GetBytes(connectionId));
            
            // Host length (2 bytes) + host
            var hostBytes = System.Text.Encoding.UTF8.GetBytes(host);
            payload.AddRange(BitConverter.GetBytes((ushort)hostBytes.Length));
            payload.AddRange(hostBytes);
            
            // Port (4 bytes)
            payload.AddRange(BitConverter.GetBytes(port));
            
            var totalLength = (ushort)(8 + payload.Count);
            var header = TdsProtocol.CreateTdsHeader(TUNNEL_CONNECT, TdsProtocol.STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(payload.ToArray(), 0, packet, 8, payload.Count);
            
            return packet;
        }
          public static byte[] CreateTunnelDataPacket(uint connectionId, byte[] data)
        {
            var payload = new List<byte>();
            
            // Connection ID (4 bytes)
            payload.AddRange(BitConverter.GetBytes(connectionId));
            
            // Data length (4 bytes) + data
            payload.AddRange(BitConverter.GetBytes(data.Length));
            payload.AddRange(data);
            
            var totalLength = (ushort)(8 + payload.Count);
            
            // Check if packet would be too large for TDS
            if (totalLength > 32768) // 32KB limit for TDS packets
            {
                throw new ArgumentException($"Tunnel data packet too large: {totalLength} bytes (max 32KB)");
            }
            
            var header = TdsProtocol.CreateTdsHeader(TUNNEL_DATA, TdsProtocol.STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(payload.ToArray(), 0, packet, 8, payload.Count);
            
            return packet;
        }
        
        public static byte[] CreateTunnelConnectAckPacket(uint connectionId, bool success, string errorMessage = "")
        {
            var payload = new List<byte>();
            
            // Connection ID (4 bytes)
            payload.AddRange(BitConverter.GetBytes(connectionId));
            
            // Success flag (1 byte)
            payload.Add(success ? (byte)1 : (byte)0);
            
            // Error message
            var errorBytes = System.Text.Encoding.UTF8.GetBytes(errorMessage);
            payload.AddRange(BitConverter.GetBytes((ushort)errorBytes.Length));
            payload.AddRange(errorBytes);
            
            var totalLength = (ushort)(8 + payload.Count);
            var header = TdsProtocol.CreateTdsHeader(TUNNEL_CONNECT_ACK, TdsProtocol.STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(payload.ToArray(), 0, packet, 8, payload.Count);
            
            return packet;
        }
        
        public static byte[] CreateTunnelDisconnectPacket(uint connectionId)
        {
            var payload = new List<byte>();
            payload.AddRange(BitConverter.GetBytes(connectionId));
            
            var totalLength = (ushort)(8 + payload.Count);
            var header = TdsProtocol.CreateTdsHeader(TUNNEL_DISCONNECT, TdsProtocol.STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(payload.ToArray(), 0, packet, 8, payload.Count);
            
            return packet;
        }
        
        public static (uint connectionId, string host, int port) ParseTunnelConnectPacket(byte[] data)
        {
            if (data.Length < 8)
                throw new ArgumentException("Invalid tunnel connect packet");
                
            var payload = new byte[data.Length - 8];
            Array.Copy(data, 8, payload, 0, payload.Length);
            
            var connectionId = BitConverter.ToUInt32(payload, 0);
            var hostLength = BitConverter.ToUInt16(payload, 4);
            var host = System.Text.Encoding.UTF8.GetString(payload, 6, hostLength);
            var port = BitConverter.ToInt32(payload, 6 + hostLength);
            
            return (connectionId, host, port);
        }
          public static (uint connectionId, byte[] data) ParseTunnelDataPacket(byte[] packet)
        {
            if (packet.Length < 16) // 8 (header) + 4 (connectionId) + 4 (dataLength)
                throw new ArgumentException("Invalid tunnel data packet - too short");
                
            var payload = new byte[packet.Length - 8];
            Array.Copy(packet, 8, payload, 0, payload.Length);
            
            var connectionId = BitConverter.ToUInt32(payload, 0);
            var dataLength = BitConverter.ToInt32(payload, 4);
            
            // Validate data length to prevent buffer overflows
            if (dataLength < 0 || dataLength > payload.Length - 8)
                throw new ArgumentException($"Invalid data length in tunnel packet: {dataLength}, available: {payload.Length - 8}");
            
            var data = new byte[dataLength];
            Array.Copy(payload, 8, data, 0, dataLength);
            
            return (connectionId, data);
        }
    }
}
