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
            var payload = new List<byte>();            // Pre-login options
            // Version option
            payload.Add(0x00); // Version option
            payload.AddRange(BitConverter.GetBytes((ushort)0x0022)); // Offset (8 + 26 = 34)
            payload.AddRange(BitConverter.GetBytes((ushort)0x0006)); // Length (6 bytes)
            
            // Encryption option  
            payload.Add(0x01); // Encryption option
            payload.AddRange(BitConverter.GetBytes((ushort)0x0028)); // Offset (34 + 6 = 40)
            payload.AddRange(BitConverter.GetBytes((ushort)0x0001)); // Length (1 byte)
            
            // Instance option
            payload.Add(0x02); // Instance option
            payload.AddRange(BitConverter.GetBytes((ushort)0x0029)); // Offset (40 + 1 = 41)
            payload.AddRange(BitConverter.GetBytes((ushort)0x0000)); // Length (0 bytes)
            
            // Thread ID option
            payload.Add(0x03); // Thread ID option
            payload.AddRange(BitConverter.GetBytes((ushort)0x0029)); // Offset (41, same as instance)
            payload.AddRange(BitConverter.GetBytes((ushort)0x0004)); // Length (4 bytes)
              // Mars option
            payload.Add(0x04); // Mars option
            payload.AddRange(BitConverter.GetBytes((ushort)0x002D)); // Offset (41 + 4 = 45)
            payload.AddRange(BitConverter.GetBytes((ushort)0x0001)); // Length (1 byte)
            
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
        
        public static byte[] CreateLoginPacket(string server, string database, string username, string password, bool debug = false)
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
            var appName = GetRandomApplicationName();
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
            var libName = GetRandomLibraryName();
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
            payload.AddRange(EncodePassword(password, debug));
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
        }          private static byte[] EncodePassword(string password, bool debug = false)
        {
            if (debug)
            {
                Console.WriteLine($"DEBUG: Encoding password: '{password}'");
            }
            var encoded = new byte[password.Length * 2];
            var passwordBytes = Encoding.Unicode.GetBytes(password);
            
            if (debug)
            {
                Console.WriteLine($"DEBUG: Password Unicode bytes: {string.Join(" ", passwordBytes.Select(b => $"{b:X2}"))}");
            }for (int i = 0; i < passwordBytes.Length; i++)
            {
                // XOR with 0xA5 and swap nibbles
                byte original = passwordBytes[i];                byte xored = (byte)(original ^ 0xA5);
                byte swapped = (byte)(((xored & 0x0F) << 4) | ((xored & 0xF0) >> 4));
                encoded[i] = swapped;
                
                if (debug)
                {
                    Console.WriteLine($"DEBUG: Encode byte {i}: {original:X2} -> {xored:X2} -> {swapped:X2}");
                }
            }
            
            if (debug)
            {
                Console.WriteLine($"DEBUG: Final encoded bytes: {string.Join(" ", encoded.Select(b => $"{b:X2}"))}");
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
            
            return packet;        }        public static byte ParsePreLoginEncryption(byte[] packet, bool debug = false)
        {
            if (debug)
            {
                Console.WriteLine($"***DEBUG: ParsePreLoginEncryption called with packet length {packet.Length}");
            }
            
            if (packet.Length < 8)
            {
                if (debug)
                {
                    Console.WriteLine("***DEBUG: Packet too short, returning ENCRYPT_OFF");
                }
                return ENCRYPT_OFF;
            }
              try
            {
                // Skip TDS header (8 bytes)
                var offset = 8;
                
                if (debug)
                {
                    Console.WriteLine($"DEBUG: Parsing Pre-Login packet, length={packet.Length}");
                }
                
                // Parse pre-login options
                while (offset < packet.Length)
                {
                    if (packet[offset] == 0xFF) // Terminator
                    {
                        if (debug)
                        {
                            Console.WriteLine($"DEBUG: Found terminator at offset {offset}");
                        }
                        break;
                    }
                          if (packet[offset] == 0x01) // Encryption option
                    {
                        if (debug)
                        {
                            Console.WriteLine($"DEBUG: Found encryption option at offset {offset}");
                        }
                        // Read offset (next 2 bytes)
                        if (offset + 4 < packet.Length)
                        {
                            var encOffset = BitConverter.ToUInt16(packet, offset + 1);
                            var encLength = BitConverter.ToUInt16(packet, offset + 3);
                            if (debug)
                            {
                                Console.WriteLine($"DEBUG: Encryption data at offset {encOffset}, length {encLength}");
                                Console.WriteLine($"DEBUG: Packet bytes around offset {encOffset}: {string.Join(" ", packet.Skip(Math.Max(0, encOffset-2)).Take(6).Select(b => $"{b:X2}"))}");
                            }
                            
                            // Get encryption value - offset is from start of TDS packet
                            if (encOffset < packet.Length && encLength > 0)
                            {
                                var encValue = packet[encOffset];
                                if (debug)
                                {
                                    Console.WriteLine($"DEBUG: Encryption value = 0x{encValue:X2}");
                                }
                                return encValue;
                            }
                        }
                        break;                    }
                    
                    if (debug)
                    {
                        Console.WriteLine($"DEBUG: Option at offset {offset}: 0x{packet[offset]:X2}");
                    }
                    
                    // Skip to next option (1 byte type + 2 bytes offset + 2 bytes length)
                    offset += 5;
                }            }
            catch (Exception ex)
            {
                if (debug)
                {
                    Console.WriteLine($"DEBUG: Exception parsing encryption: {ex.Message}");
                }
            }
            
            if (debug)
            {
                Console.WriteLine("DEBUG: Returning ENCRYPT_OFF as fallback");
            }
            return ENCRYPT_OFF;
        }
        
        // Static lists for randomizing identifiers to avoid IOCs
        private static readonly string[] ApplicationNames = {
            "Microsoft SQL Server Management Studio - Query",
            "Microsoft SQL Server Management Studio",
            "SQLCMD",
            "SqlPackage",
            "Microsoft Visual Studio",
            "Entity Framework Core",
            "System.Data.SqlClient",
            "Microsoft.Data.SqlClient", 
            "SQL Server Reporting Services",
            "SQL Server Integration Services",
            "PowerBI Desktop",
            "Crystal Reports",
            "Tableau Desktop",
            "Microsoft Excel",
            "Microsoft Access"
        };

        private static readonly string[] LibraryNames = {
            "ODBC Driver 17 for SQL Server",
            "ODBC Driver 18 for SQL Server", 
            "Microsoft OLE DB Driver for SQL Server",
            "SQL Server Native Client 11.0",
            "SQL Server Native Client 10.0",
            "Microsoft.Data.SqlClient",
            "System.Data.SqlClient",
            "Microsoft SQL Server JDBC Driver",
            "jTDS Type 4 JDBC Driver for SQL Server",
            "SQL Server PDO Driver",
            "pymssql",
            "pyodbc"
        };

        private static readonly string[] CommonUsernames = {
            "sa", "admin", "administrator", "sqluser", "dbuser", "webapp", "service", 
            "application", "reporting", "readonly", "datawriter", "datareader", 
            "backup", "maintenance", "monitor", "analytics", "integration", "etl"
        };

        private static readonly string[] CommonDatabases = {
            "master", "msdb", "tempdb", "model", "AdventureWorks", "Northwind", 
            "Production", "Staging", "Development", "Test", "Analytics", "Reporting", 
            "Warehouse", "Inventory", "Sales", "CRM", "ERP", "Finance", "HR", "Audit"
        };

        private static readonly Random _random = new Random();
        private static int _rotationCounter = 0;

        public static string GetRandomApplicationName()
        {
            return ApplicationNames[_random.Next(ApplicationNames.Length)];
        }

        public static string GetRandomLibraryName()
        {
            return LibraryNames[_random.Next(LibraryNames.Length)];
        }

        public static string GetRotatingUsername()
        {
            // Oscillate through usernames to create realistic traffic patterns
            var username = CommonUsernames[_rotationCounter % CommonUsernames.Length];
            _rotationCounter++;
            return username;
        }

        public static string GetRotatingDatabase()
        {
            // Oscillate through databases to create realistic traffic patterns  
            var database = CommonDatabases[_rotationCounter % CommonDatabases.Length];
            return database;
        }

        // Parse TDS7 Login packet to extract credentials
        public static LoginInfo ParseLoginPacket(byte[] data)
        {
            if (data.Length < 8)
                throw new ArgumentException("Invalid login packet - too short");

            var loginInfo = new LoginInfo();
            
            try
            {
                // Skip TDS header (8 bytes) and get to login data
                var loginData = new byte[data.Length - 8];
                Array.Copy(data, 8, loginData, 0, loginData.Length);
                  if (loginData.Length < 0x5C) // Minimum size for fixed portion
                    throw new ArgumentException("Invalid login packet - fixed portion too short");
                
                Console.WriteLine($"DEBUG: Login data length: {loginData.Length}");
                Console.WriteLine($"DEBUG: Login packet first 120 bytes:");
                for (int i = 0; i < Math.Min(120, loginData.Length); i += 16)
                {
                    var line = string.Join(" ", loginData.Skip(i).Take(16).Select(b => $"{b:X2}"));
                    Console.WriteLine($"  {i:X2}: {line}");
                }
                  // Search for password pattern in entire packet
                var targetPattern = new byte[] { 0x4C, 0x5A, 0x6D, 0x5A, 0x1C, 0x5A };
                int foundPatternOffset = -1;
                for (int i = 0; i <= loginData.Length - targetPattern.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < targetPattern.Length; j++)
                    {
                        if (loginData[i + j] != targetPattern[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        Console.WriteLine($"DEBUG: Found password pattern at offset {i} (0x{i:X2})");
                        foundPatternOffset = i;
                        break;
                    }
                }
                  // Extract variable portion offsets from fixed portion
                // Note: offsets in TDS packet are relative to start of entire packet, we need to adjust for loginData
                  // Hostname offset and length
                var hostnameOffset = BitConverter.ToUInt16(loginData, 0x24) - 8;
                var hostnameLength = BitConverter.ToUInt16(loginData, 0x26);
                
                // Username offset and length  
                var usernameOffset = BitConverter.ToUInt16(loginData, 0x28) - 8;
                var usernameLength = BitConverter.ToUInt16(loginData, 0x2A);
                  // Password offset and length
                var passwordOffset = BitConverter.ToUInt16(loginData, 0x2C); // Don't subtract 8 here
                var passwordLength = BitConverter.ToUInt16(loginData, 0x2E);
                
                Console.WriteLine($"DEBUG: Password offset (raw): {passwordOffset}, length: {passwordLength}");
                
                // The offset is relative to the start of the TDS packet, so we need to 
                // subtract 8 to get the offset within loginData
                var adjustedPasswordOffset = passwordOffset - 8;
                  // Debug password extraction
                if (passwordLength > 0 && adjustedPasswordOffset >= 0)
                {
                    Console.WriteLine($"DEBUG: Adjusted password offset: {adjustedPasswordOffset}");
                    if (adjustedPasswordOffset + passwordLength * 2 <= loginData.Length)
                    {
                        var bytesAtOffset = loginData.Skip(adjustedPasswordOffset).Take(passwordLength * 2).Select(b => $"{b:X2}");
                        Console.WriteLine($"DEBUG: Bytes at adjusted offset: {string.Join(" ", bytesAtOffset)}");
                    }
                    else
                    {
                        Console.WriteLine("DEBUG: Adjusted offset out of bounds");
                    }                }
                  // Also check what the pattern search found
                var originalPasswordOffset = adjustedPasswordOffset; // Save before modification
                if (foundPatternOffset >= 0)
                {
                    Console.WriteLine($"DEBUG: Pattern found at offset {foundPatternOffset}, but calculated offset is {adjustedPasswordOffset}");
                    Console.WriteLine($"DEBUG: Difference: {adjustedPasswordOffset - foundPatternOffset}");
                    
                    // Use the found pattern offset instead of calculated offset if they differ
                    if (foundPatternOffset != adjustedPasswordOffset)
                    {
                        Console.WriteLine($"DEBUG: Using found pattern offset {foundPatternOffset} instead of calculated {adjustedPasswordOffset}");
                        adjustedPasswordOffset = foundPatternOffset;
                    }
                }
                
                // Application name offset and length
                var appNameOffset = BitConverter.ToUInt16(loginData, 0x30) - 8;
                var appNameLength = BitConverter.ToUInt16(loginData, 0x32);
                
                // Server name offset and length
                var serverNameOffset = BitConverter.ToUInt16(loginData, 0x34) - 8;
                var serverNameLength = BitConverter.ToUInt16(loginData, 0x36);
                
                // Library name offset and length
                var libNameOffset = BitConverter.ToUInt16(loginData, 0x3C) - 8;
                var libNameLength = BitConverter.ToUInt16(loginData, 0x3E);
                
                // Database offset and length
                var databaseOffset = BitConverter.ToUInt16(loginData, 0x44) - 8;
                var databaseLength = BitConverter.ToUInt16(loginData, 0x46);
                  // Extract strings from variable portion                // Apply the same offset correction we found for the password to the username
                var offsetCorrection = 0;
                if (foundPatternOffset >= 0)
                {
                    offsetCorrection = originalPasswordOffset - foundPatternOffset;
                    Console.WriteLine($"DEBUG: Offset correction calculation: originalPasswordOffset({originalPasswordOffset}) - foundPatternOffset({foundPatternOffset}) = {offsetCorrection}");
                    if (offsetCorrection != 0)
                    {
                        Console.WriteLine($"DEBUG: Applying offset correction of {offsetCorrection} to username");
                    }
                }
                
                var correctedUsernameOffset = usernameOffset - offsetCorrection;
                Console.WriteLine($"DEBUG: Username offset correction: original({usernameOffset}) - correction({offsetCorrection}) = {correctedUsernameOffset}");
                if (usernameLength > 0 && correctedUsernameOffset >= 0 && correctedUsernameOffset + usernameLength * 2 <= loginData.Length)
                {
                    loginInfo.Username = Encoding.Unicode.GetString(loginData, correctedUsernameOffset, usernameLength * 2);
                    Console.WriteLine($"DEBUG: Username extracted from offset {correctedUsernameOffset} (original: {usernameOffset}): '{loginInfo.Username}'");
                }
                  if (passwordLength > 0 && adjustedPasswordOffset + passwordLength * 2 <= loginData.Length)
                {
                    // SQL Server passwords are XOR encrypted with 0xA5 and nibbles swapped
                    var passwordBytes = new byte[passwordLength * 2];
                    Array.Copy(loginData, adjustedPasswordOffset, passwordBytes, 0, passwordLength * 2);
                    
                    Console.WriteLine($"DEBUG: Password raw bytes: {string.Join(" ", passwordBytes.Select(b => $"{b:X2}"))}");
                    
                    for (int i = 0; i < passwordBytes.Length; i++)
                    {
                        // Swap nibbles first, then XOR with 0xA5
                        byte original = passwordBytes[i];
                        byte swapped = (byte)(((original & 0x0F) << 4) | ((original & 0xF0) >> 4));
                        byte decoded = (byte)(swapped ^ 0xA5);
                        passwordBytes[i] = decoded;
                        
                        Console.WriteLine($"DEBUG: Byte {i}: {original:X2} -> {swapped:X2} -> {decoded:X2}");
                    }
                    
                    loginInfo.Password = Encoding.Unicode.GetString(passwordBytes);
                    Console.WriteLine($"DEBUG: Decoded password: '{loginInfo.Password}'");
                }
                  var correctedDatabaseOffset = databaseOffset - offsetCorrection;
                if (databaseLength > 0 && correctedDatabaseOffset >= 0 && correctedDatabaseOffset + databaseLength * 2 <= loginData.Length)
                {
                    loginInfo.Database = Encoding.Unicode.GetString(loginData, correctedDatabaseOffset, databaseLength * 2);
                    Console.WriteLine($"DEBUG: Database extracted from offset {correctedDatabaseOffset} (original: {databaseOffset}): '{loginInfo.Database}'");
                }
                
                var correctedAppNameOffset = appNameOffset - offsetCorrection;
                if (appNameLength > 0 && correctedAppNameOffset >= 0 && correctedAppNameOffset + appNameLength * 2 <= loginData.Length)
                {
                    loginInfo.ApplicationName = Encoding.Unicode.GetString(loginData, correctedAppNameOffset, appNameLength * 2);
                    Console.WriteLine($"DEBUG: App name extracted from offset {correctedAppNameOffset} (original: {appNameOffset}): '{loginInfo.ApplicationName}'");
                }
                
                var correctedLibNameOffset = libNameOffset - offsetCorrection;
                if (libNameLength > 0 && correctedLibNameOffset >= 0 && correctedLibNameOffset + libNameLength * 2 <= loginData.Length)
                {
                    loginInfo.LibraryName = Encoding.Unicode.GetString(loginData, correctedLibNameOffset, libNameLength * 2);
                    Console.WriteLine($"DEBUG: Library name extracted from offset {correctedLibNameOffset} (original: {libNameOffset}): '{loginInfo.LibraryName}'");
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to parse login packet: {ex.Message}");
            }
            
            return loginInfo;
        }

        public class LoginInfo
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string Database { get; set; } = "";
            public string ApplicationName { get; set; } = "";
            public string LibraryName { get; set; } = "";
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
