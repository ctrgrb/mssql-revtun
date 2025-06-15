using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace RevTun
{
    // TDS (Tabular Data Stream) Protocol Implementation
    public static class TdsProtocol
    {
        // TDS Message Types
        public const byte SQL_BATCH = 0x01;
        public const byte PRE_TDS7_LOGIN = 0x02;
        public const byte RPC = 0x03;
        public const byte TABULAR_RESULT = 0x04;
        public const byte ATTENTION_SIGNAL = 0x06;
        public const byte BULK_LOAD_DATA = 0x07;
        public const byte FEDERATED_AUTH_TOKEN = 0x08;
        public const byte TRANSACTION_MANAGER = 0x0E;
        public const byte TDS7_LOGIN = 0x10;
        public const byte SSPI = 0x11;
        public const byte PRE_LOGIN = 0x12;
        
        // TDS Status Bits
        public const byte STATUS_NORMAL = 0x00;
        public const byte STATUS_EOM = 0x01;  // End of Message
        public const byte STATUS_IGNORE = 0x02;
        public const byte STATUS_RESET_CONNECTION = 0x08;
        public const byte STATUS_RESET_CONNECTION_SKIP_TRAN = 0x10;
        
        public static byte[] CreateTdsHeader(byte type, byte status, ushort length, ushort spid, byte packetId, byte window)
        {
            var header = new byte[8];
            header[0] = type;
            header[1] = status;
            header[2] = (byte)(length >> 8);    // Length high byte
            header[3] = (byte)(length & 0xFF);  // Length low byte
            header[4] = (byte)(spid >> 8);      // SPID high byte
            header[5] = (byte)(spid & 0xFF);    // SPID low byte
            header[6] = packetId;
            header[7] = window;
            return header;
        }
        
        public static TdsHeader ParseTdsHeader(byte[] data)
        {
            if (data.Length < 8)
                throw new ArgumentException("Invalid TDS header length");
                
            return new TdsHeader
            {
                Type = data[0],
                Status = data[1],
                Length = (ushort)((data[2] << 8) | data[3]),
                Spid = (ushort)((data[4] << 8) | data[5]),
                PacketId = data[6],
                Window = data[7]
            };
        }
        
        // TDS Encryption Types
        public const byte ENCRYPT_NOT_SUP = 0x00;    // Encryption not supported
        public const byte ENCRYPT_OFF = 0x01;        // Encryption off
        public const byte ENCRYPT_ON = 0x02;         // Encryption on
        public const byte ENCRYPT_REQ = 0x03;        // Encryption required
          public static byte[] CreatePreLoginPacket(byte encryptionMode = ENCRYPT_ON)
        {
            var payload = new List<byte>();
            
            // Pre-login options
            // Version option
            payload.Add(0x00); // Version option
            payload.AddRange(BitConverter.GetBytes((ushort)0x001A)); // Offset
            payload.AddRange(BitConverter.GetBytes((ushort)0x0006)); // Length
            
            // Encryption option
            payload.Add(0x01); // Encryption option
            payload.AddRange(BitConverter.GetBytes((ushort)0x0020)); // Offset
            payload.AddRange(BitConverter.GetBytes((ushort)0x0001)); // Length
            
            // Instance option
            payload.Add(0x02); // Instance option
            payload.AddRange(BitConverter.GetBytes((ushort)0x0021)); // Offset
            payload.AddRange(BitConverter.GetBytes((ushort)0x0000)); // Length
            
            // Thread ID option
            payload.Add(0x03); // Thread ID option
            payload.AddRange(BitConverter.GetBytes((ushort)0x0021)); // Offset
            payload.AddRange(BitConverter.GetBytes((ushort)0x0004)); // Length
              // Mars option
            payload.Add(0x04); // Mars option
            payload.AddRange(BitConverter.GetBytes((ushort)0x0025)); // Offset
            payload.AddRange(BitConverter.GetBytes((ushort)0x0001)); // Length
            
            // Terminator
            payload.Add(0xFF);
            
            // Version data (6 bytes)
            payload.AddRange(new byte[] { 0x10, 0x00, 0x07, 0xD0, 0x00, 0x00 }); // SQL Server 2016
              // Encryption (1 byte) - Use provided encryption mode
            payload.Add(encryptionMode);
            
            // Thread ID (4 bytes)
            payload.AddRange(BitConverter.GetBytes(Environment.CurrentManagedThreadId));
            
            // Mars (1 byte) - OFF
            payload.Add(0x00);
            
            var totalLength = (ushort)(8 + payload.Count);
            var header = CreateTdsHeader(PRE_LOGIN, STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(payload.ToArray(), 0, packet, 8, payload.Count);
            
            return packet;
        }
        
        public static byte[] CreateLoginPacket(string server, string database, string username, string password)
        {
            var payload = new List<byte>();
            
            // Login7 fixed portion
            payload.AddRange(BitConverter.GetBytes((uint)0x0000005C)); // Length of total packet
            payload.AddRange(BitConverter.GetBytes((uint)0x74000004)); // TDS Version
            payload.AddRange(BitConverter.GetBytes((uint)0x00000800)); // Packet size
            payload.AddRange(BitConverter.GetBytes((uint)0x00000007)); // Client version
            payload.AddRange(BitConverter.GetBytes((uint)Process.GetCurrentProcess().Id)); // Client PID
            payload.AddRange(BitConverter.GetBytes((uint)0x00000000)); // Connection ID
            payload.Add(0x01); // Option flags 1 - INIT_LANG_FATAL
            payload.Add(0x00); // Option flags 2
            payload.Add(0x00); // SQL type flags
            payload.Add(0x00); // Option flags 3
            payload.AddRange(BitConverter.GetBytes((uint)0x00000000)); // Client time zone
            payload.AddRange(BitConverter.GetBytes((uint)0x00000409)); // Client LCID
            
            // Variable portion offsets and lengths
            var currentOffset = 0x5C; // Start after fixed portion
            
            // Hostname
            payload.AddRange(BitConverter.GetBytes((ushort)currentOffset));
            var hostname = Environment.MachineName;
            payload.AddRange(BitConverter.GetBytes((ushort)hostname.Length));
            currentOffset += hostname.Length * 2;
            
            // Username
            payload.AddRange(BitConverter.GetBytes((ushort)currentOffset));
            payload.AddRange(BitConverter.GetBytes((ushort)username.Length));
            currentOffset += username.Length * 2;
            
            // Password
            payload.AddRange(BitConverter.GetBytes((ushort)currentOffset));
            payload.AddRange(BitConverter.GetBytes((ushort)password.Length));
            currentOffset += password.Length * 2;
            
            // App name
            payload.AddRange(BitConverter.GetBytes((ushort)currentOffset));
            var appName = "RevTun";
            payload.AddRange(BitConverter.GetBytes((ushort)appName.Length));
            currentOffset += appName.Length * 2;
            
            // Server name
            payload.AddRange(BitConverter.GetBytes((ushort)currentOffset));
            payload.AddRange(BitConverter.GetBytes((ushort)server.Length));
            currentOffset += server.Length * 2;
            
            // Unused
            payload.AddRange(BitConverter.GetBytes((ushort)0));
            payload.AddRange(BitConverter.GetBytes((ushort)0));
            
            // Library name
            payload.AddRange(BitConverter.GetBytes((ushort)currentOffset));
            var libName = "RevTun-TDS";
            payload.AddRange(BitConverter.GetBytes((ushort)libName.Length));
            currentOffset += libName.Length * 2;
            
            // Language
            payload.AddRange(BitConverter.GetBytes((ushort)0));
            payload.AddRange(BitConverter.GetBytes((ushort)0));
            
            // Database
            payload.AddRange(BitConverter.GetBytes((ushort)currentOffset));
            payload.AddRange(BitConverter.GetBytes((ushort)database.Length));
            
            // Add variable data
            payload.AddRange(Encoding.Unicode.GetBytes(hostname));
            payload.AddRange(Encoding.Unicode.GetBytes(username));
            payload.AddRange(EncodePassword(password));
            payload.AddRange(Encoding.Unicode.GetBytes(appName));
            payload.AddRange(Encoding.Unicode.GetBytes(server));
            payload.AddRange(Encoding.Unicode.GetBytes(libName));
            payload.AddRange(Encoding.Unicode.GetBytes(database));
            
            var totalLength = (ushort)(8 + payload.Count);
            var header = CreateTdsHeader(TDS7_LOGIN, STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(payload.ToArray(), 0, packet, 8, payload.Count);
            
            return packet;
        }
        
        private static byte[] EncodePassword(string password)
        {
            var encoded = new byte[password.Length * 2];
            var passwordBytes = Encoding.Unicode.GetBytes(password);
            
            for (int i = 0; i < passwordBytes.Length; i++)
            {
                // XOR with 0xA5 and swap nibbles
                byte b = passwordBytes[i];
                b ^= 0xA5;
                encoded[i] = (byte)(((b & 0x0F) << 4) | ((b & 0xF0) >> 4));
            }
            
            return encoded;
        }
        
        public static byte[] CreateSqlBatchPacket(string sql)
        {
            var sqlBytes = Encoding.Unicode.GetBytes(sql);
            var totalLength = (ushort)(8 + sqlBytes.Length);
            var header = CreateTdsHeader(SQL_BATCH, STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(sqlBytes, 0, packet, 8, sqlBytes.Length);
            
            return packet;
        }
        
        public static byte[] CreateTabularResultPacket(string[][] rows, string[] columnNames)
        {
            var payload = new List<byte>();
            
            // Token: COLMETADATA (0x81)
            payload.Add(0x81);
            payload.AddRange(BitConverter.GetBytes((ushort)columnNames.Length));
            
            // Column metadata
            foreach (var columnName in columnNames)
            {
                payload.AddRange(BitConverter.GetBytes((uint)0x00000000)); // UserType
                payload.AddRange(BitConverter.GetBytes((ushort)0x0000)); // Flags
                payload.Add(0xE7); // TYPE_INFO - NVARCHARTYPE
                payload.AddRange(BitConverter.GetBytes((ushort)0x1000)); // MaxLength
                payload.Add(0x00); // Collation (5 bytes)
                payload.Add(0x00);
                payload.Add(0x00);
                payload.Add(0x00);
                payload.Add(0x00);
                payload.Add((byte)columnName.Length); // Column name length
                payload.AddRange(Encoding.Unicode.GetBytes(columnName));
            }
            
            // Rows
            foreach (var row in rows)
            {
                payload.Add(0xD1); // Token: ROW
                
                foreach (var cell in row)
                {
                    if (cell == null)
                    {
                        payload.AddRange(BitConverter.GetBytes((ushort)0xFFFF)); // NULL
                    }
                    else
                    {
                        var cellBytes = Encoding.Unicode.GetBytes(cell);
                        payload.AddRange(BitConverter.GetBytes((ushort)cellBytes.Length));
                        payload.AddRange(cellBytes);
                    }
                }
            }
            
            // Token: DONE (0xFD)
            payload.Add(0xFD);
            payload.AddRange(BitConverter.GetBytes((ushort)0x0000)); // Status
            payload.AddRange(BitConverter.GetBytes((ushort)0x0000)); // CurCmd
            payload.AddRange(BitConverter.GetBytes((uint)rows.Length)); // RowCount
            
            var totalLength = (ushort)(8 + payload.Count);
            var header = CreateTdsHeader(TABULAR_RESULT, STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(payload.ToArray(), 0, packet, 8, payload.Count);
            
            return packet;
        }
        
        public static byte ParsePreLoginEncryption(byte[] packet)
        {
            if (packet.Length < 8)
                return ENCRYPT_OFF;
            
            try
            {
                // Skip TDS header (8 bytes)
                var offset = 8;
                
                // Parse pre-login options
                while (offset < packet.Length)
                {
                    if (packet[offset] == 0xFF) // Terminator
                        break;
                        
                    if (packet[offset] == 0x01) // Encryption option
                    {
                        // Read offset (next 2 bytes)
                        if (offset + 2 < packet.Length)
                        {
                            var encOffset = BitConverter.ToUInt16(packet, offset + 1);
                            // Read length (next 2 bytes) 
                            if (offset + 4 < packet.Length)
                            {
                                var encLength = BitConverter.ToUInt16(packet, offset + 3);
                                // Get encryption value
                                if (encOffset < packet.Length && encLength > 0)
                                {
                                    return packet[encOffset];
                                }
                            }
                        }
                        break;
                    }
                    
                    // Skip to next option (1 byte type + 2 bytes offset + 2 bytes length)
                    offset += 5;
                }
            }
            catch
            {
                // If parsing fails, assume no encryption
            }
            
            return ENCRYPT_OFF;
        }
    }
    
    public class TdsHeader
    {
        public byte Type { get; set; }
        public byte Status { get; set; }
        public ushort Length { get; set; }
        public ushort Spid { get; set; }
        public byte PacketId { get; set; }
        public byte Window { get; set; }
    }
}
