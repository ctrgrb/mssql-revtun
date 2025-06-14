using System.Text;

namespace RevTun
{
    public static class MssqlTrafficUtils
    {
        public static void PrintWelcomeMessage()
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                    RevTun - MSSQL Traffic Simulator         ║");
            Console.WriteLine("║                                                              ║");
            Console.WriteLine("║  This tool simulates MSSQL network traffic using the        ║");
            Console.WriteLine("║  Tabular Data Stream (TDS) protocol.                        ║");
            Console.WriteLine("║                                                              ║");
            Console.WriteLine("║  Features:                                                   ║");
            Console.WriteLine("║  • Authentic TDS protocol implementation                    ║");
            Console.WriteLine("║  • Pre-Login and Login handshake                           ║");
            Console.WriteLine("║  • SQL Batch execution                                      ║");
            Console.WriteLine("║  • Tabular result sets                                      ║");
            Console.WriteLine("║  • Bidirectional communication                              ║");
            Console.WriteLine("║  • Packet logging and analysis                              ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
        }
        
        public static void PrintUsageInstructions()
        {
            Console.WriteLine("Usage Instructions:");
            Console.WriteLine("==================");
            Console.WriteLine();
            Console.WriteLine("1. Start the Server:");
            Console.WriteLine("   - Run the program and select option '1'");
            Console.WriteLine("   - Server will listen on port 1433 (default MSSQL port)");
            Console.WriteLine("   - Server will accept multiple client connections");
            Console.WriteLine();
            Console.WriteLine("2. Start the Client:");
            Console.WriteLine("   - Run the program in another terminal and select option '2'");
            Console.WriteLine("   - Client will connect to localhost:1433");
            Console.WriteLine("   - Performs TDS handshake automatically");
            Console.WriteLine("   - Enters interactive SQL session");
            Console.WriteLine();
            Console.WriteLine("3. Send SQL Commands:");
            Console.WriteLine("   - Type SQL commands at the 'SQL>' prompt");
            Console.WriteLine("   - Commands are sent as TDS SQL_BATCH packets");
            Console.WriteLine("   - Server responds with tabular results");
            Console.WriteLine("   - Type 'exit' to quit the client");
            Console.WriteLine();
            Console.WriteLine("4. Traffic Analysis:");
            Console.WriteLine("   - All TDS packets are logged with hex dumps");
            Console.WriteLine("   - Packet types, lengths, and structure are displayed");
            Console.WriteLine("   - Perfect for analyzing MSSQL protocol behavior");
            Console.WriteLine();
        }
        
        public static string GenerateSampleSqlQuery(int queryType)
        {
            return queryType switch
            {
                1 => "SELECT @@VERSION",
                2 => "SELECT * FROM sys.databases",
                3 => "SELECT name, database_id, create_date FROM sys.databases",
                4 => "SELECT @@SERVERNAME, @@SERVICENAME, @@SPID",
                5 => "SELECT GETDATE() as CurrentTime",
                6 => "SELECT COUNT(*) FROM sys.objects",
                _ => "SELECT 'Hello from RevTun' as Message"
            };
        }
        
        public static void PrintTdsPacketAnalysis(byte[] packet)
        {
            if (packet.Length < 8)
            {
                Console.WriteLine("Invalid TDS packet (too short)");
                return;
            }
            
            Console.WriteLine("TDS Packet Analysis:");
            Console.WriteLine("====================");
            
            var header = TdsProtocol.ParseTdsHeader(packet);
            
            Console.WriteLine($"Header Analysis:");
            Console.WriteLine($"  Type: 0x{header.Type:X2} ({GetTdsMessageTypeDescription(header.Type)})");
            Console.WriteLine($"  Status: 0x{header.Status:X2} ({GetTdsStatusDescription(header.Status)})");
            Console.WriteLine($"  Length: {header.Length} bytes");
            Console.WriteLine($"  SPID: {header.Spid}");
            Console.WriteLine($"  Packet ID: {header.PacketId}");
            Console.WriteLine($"  Window: {header.Window}");
            Console.WriteLine();
            
            if (packet.Length > 8)
            {
                Console.WriteLine("Payload Analysis:");
                var payload = new byte[packet.Length - 8];
                Array.Copy(packet, 8, payload, 0, payload.Length);
                
                Console.WriteLine($"  Payload Length: {payload.Length} bytes");
                
                // Show hex dump in formatted rows
                Console.WriteLine("  Hex Dump:");
                for (int i = 0; i < payload.Length; i += 16)
                {
                    var chunk = payload.Skip(i).Take(16).ToArray();
                    var hex = BitConverter.ToString(chunk).Replace("-", " ");
                    var ascii = GetAsciiRepresentation(chunk);
                    Console.WriteLine($"    {i:X4}: {hex,-47} {ascii}");
                }
                
                // Try to parse specific message types
                switch (header.Type)
                {
                    case TdsProtocol.PRE_LOGIN:
                        AnalyzePreLoginPacket(payload);
                        break;
                    case TdsProtocol.TDS7_LOGIN:
                        AnalyzeLoginPacket(payload);
                        break;
                    case TdsProtocol.SQL_BATCH:
                        AnalyzeSqlBatchPacket(payload);
                        break;
                    case TdsProtocol.TABULAR_RESULT:
                        AnalyzeTabularResultPacket(payload);
                        break;
                }
            }
            
            Console.WriteLine();
        }
        
        private static string GetTdsMessageTypeDescription(byte type)
        {
            return type switch
            {
                0x01 => "SQL Batch",
                0x02 => "Pre-TDS7 Login",
                0x03 => "RPC (Remote Procedure Call)",
                0x04 => "Tabular Result",
                0x06 => "Attention Signal",
                0x07 => "Bulk Load Data",
                0x08 => "Federated Authentication Token",
                0x0E => "Transaction Manager Request",
                0x10 => "TDS7 Login",
                0x11 => "SSPI Message",
                0x12 => "Pre-Login",
                _ => "Unknown/Reserved"
            };
        }
        
        private static string GetTdsStatusDescription(byte status)
        {
            var descriptions = new List<string>();
            
            if ((status & 0x01) != 0) descriptions.Add("End of Message");
            if ((status & 0x02) != 0) descriptions.Add("Ignore this event");
            if ((status & 0x04) != 0) descriptions.Add("Event notification");
            if ((status & 0x08) != 0) descriptions.Add("Reset connection");
            if ((status & 0x10) != 0) descriptions.Add("Reset connection (skip tran)");
            
            return descriptions.Count > 0 ? string.Join(", ", descriptions) : "Normal";
        }
        
        private static string GetAsciiRepresentation(byte[] bytes)
        {
            var sb = new StringBuilder();
            foreach (var b in bytes)
            {
                sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }
            return sb.ToString();
        }
        
        private static void AnalyzePreLoginPacket(byte[] payload)
        {
            Console.WriteLine("  Pre-Login Packet Analysis:");
            Console.WriteLine("    Contains version, encryption, and connection options");
        }
        
        private static void AnalyzeLoginPacket(byte[] payload)
        {
            Console.WriteLine("  Login Packet Analysis:");
            Console.WriteLine("    Contains authentication and connection parameters");
        }
        
        private static void AnalyzeSqlBatchPacket(byte[] payload)
        {
            Console.WriteLine("  SQL Batch Packet Analysis:");
            try
            {
                var sql = Encoding.Unicode.GetString(payload).TrimEnd('\0');
                Console.WriteLine($"    SQL Statement: {sql}");
            }
            catch
            {
                Console.WriteLine("    Unable to decode SQL statement");
            }
        }
        
        private static void AnalyzeTabularResultPacket(byte[] payload)
        {
            Console.WriteLine("  Tabular Result Packet Analysis:");
            Console.WriteLine("    Contains query results, metadata, and status tokens");
        }
        
        public static void PrintNetworkTrafficSimulationOptions()
        {
            Console.WriteLine("Network Traffic Simulation Options:");
            Console.WriteLine("===================================");
            Console.WriteLine();
            Console.WriteLine("1. Basic Connection Test");
            Console.WriteLine("   - Establishes TDS connection");
            Console.WriteLine("   - Performs handshake");
            Console.WriteLine("   - Sends simple queries");
            Console.WriteLine();
            Console.WriteLine("2. Bulk Data Transfer");
            Console.WriteLine("   - Simulates large result sets");
            Console.WriteLine("   - Tests packet fragmentation");
            Console.WriteLine("   - Measures throughput");
            Console.WriteLine();
            Console.WriteLine("3. Authentication Scenarios");
            Console.WriteLine("   - Tests different auth methods");
            Console.WriteLine("   - Simulates login failures");
            Console.WriteLine("   - SSPI integration");
            Console.WriteLine();
            Console.WriteLine("4. Error Condition Testing");
            Console.WriteLine("   - Malformed packets");
            Console.WriteLine("   - Connection drops");
            Console.WriteLine("   - Timeout scenarios");
            Console.WriteLine();
        }
    }
}
