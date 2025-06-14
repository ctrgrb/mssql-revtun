using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

namespace RevTun
{    public class MssqlClient
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly ClientOptions _options;
        private readonly ConcurrentDictionary<uint, TcpClient> _tunnelConnections = new();
        private bool _isConnected = false;
        
        public MssqlClient(ClientOptions options)
        {
            _options = options;
        }        public async Task ConnectAsync()
        {
            try
            {
                Console.WriteLine($"Connecting to MSSQL Server at {_options.Host}:{_options.Port}...");
                
                _client = new TcpClient();
                await _client.ConnectAsync(_options.Host, _options.Port);
                _stream = _client.GetStream();
                _isConnected = true;
                
                Console.WriteLine("Connected to server!");
                
                // Perform TDS handshake
                await PerformHandshake();
                
                // Start background task to handle tunnel messages
                _ = Task.Run(HandleTunnelMessages);
                
                // Start interactive session
                if (!_options.AutoExit)
                {
                    await StartInteractiveSession();
                }
                else
                {
                    Console.WriteLine("Auto-exit mode: Connection test completed successfully.");
                    // Wait a bit to ensure tunnel is established
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                _stream?.Close();
                _client?.Close();
                
                // Close all tunnel connections
                foreach (var tunnelConnection in _tunnelConnections.Values)
                {
                    tunnelConnection.Close();
                }
                _tunnelConnections.Clear();
            }
        }
          private async Task HandleTunnelMessages()
        {
            try
            {
                while (_isConnected && _stream != null)
                {
                    var packet = await ReceiveTdsPacket();
                    if (packet == null)
                        break;
                        
                    // Check if this is a tunnel message
                    if (packet.Length >= 8)
                    {
                        var header = TdsProtocol.ParseTdsHeader(packet);
                        
                        switch (header.Type)
                        {
                            case TunnelProtocol.TUNNEL_CONNECT:
                                await HandleTunnelConnect(packet);
                                break;
                                
                            case TunnelProtocol.TUNNEL_DATA:
                                await HandleTunnelData(packet);
                                break;
                                
                            case TunnelProtocol.TUNNEL_DISCONNECT:
                                await HandleTunnelDisconnect(packet);
                                break;
                                
                            case TdsProtocol.TABULAR_RESULT:
                                // Regular SQL response
                                Console.WriteLine("Query executed successfully!");
                                LogTdsPacket(packet, "RECEIVED");
                                ParseTabularResult(packet);
                                break;
                                
                            default:
                                Console.WriteLine($"Received TDS message type: 0x{header.Type:X2}");
                                LogTdsPacket(packet, "RECEIVED");
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling tunnel messages: {ex.Message}");
            }
        }
        
        private async Task HandleTunnelConnect(byte[] data)
        {
            try
            {
                var (connectionId, host, port) = TunnelProtocol.ParseTunnelConnectPacket(data);
                Console.WriteLine($"Tunnel connect request: {connectionId} -> {host}:{port}");
                
                var targetClient = new TcpClient();
                bool connected = false;
                string errorMessage = "";
                
                try
                {
                    await targetClient.ConnectAsync(host, port);
                    connected = true;
                    _tunnelConnections[connectionId] = targetClient;
                    
                    Console.WriteLine($"Successfully connected to {host}:{port} for tunnel {connectionId}");
                    
                    // Start forwarding data from target back to server
                    _ = Task.Run(() => ForwardTunnelData(connectionId, targetClient));
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    Console.WriteLine($"Failed to connect to {host}:{port}: {ex.Message}");
                    targetClient.Close();
                }
                
                // Send acknowledgment back to server
                var ackPacket = TunnelProtocol.CreateTunnelConnectAckPacket(connectionId, connected, errorMessage);
                if (_stream != null)
                {
                    await _stream.WriteAsync(ackPacket, 0, ackPacket.Length);
                    LogTdsPacket(ackPacket, "SENT TUNNEL ACK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling tunnel connect: {ex.Message}");
            }
        }
        
        private async Task HandleTunnelData(byte[] data)
        {
            try
            {
                var (connectionId, tunnelData) = TunnelProtocol.ParseTunnelDataPacket(data);
                
                if (_tunnelConnections.TryGetValue(connectionId, out var targetClient) && targetClient.Connected)
                {
                    var targetStream = targetClient.GetStream();
                    await targetStream.WriteAsync(tunnelData, 0, tunnelData.Length);
                    Console.WriteLine($"Forwarded {tunnelData.Length} bytes to target for connection {connectionId}");
                }
                else
                {
                    Console.WriteLine($"Tunnel data received for unknown/closed connection {connectionId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling tunnel data: {ex.Message}");
            }
        }
          private Task HandleTunnelDisconnect(byte[] data)
        {
            try
            {
                var payload = new byte[data.Length - 8];
                Array.Copy(data, 8, payload, 0, payload.Length);
                var connectionId = BitConverter.ToUInt32(payload, 0);
                
                if (_tunnelConnections.TryRemove(connectionId, out var targetClient))
                {
                    targetClient.Close();
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"Tunnel connection {connectionId} disconnected");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling tunnel disconnect: {ex.Message}");
            }
            return Task.CompletedTask;
        }
        
        private async Task ForwardTunnelData(uint connectionId, TcpClient targetClient)
        {
            var buffer = new byte[4096];
            var targetStream = targetClient.GetStream();
            
            try
            {
                while (targetClient.Connected && _isConnected)
                {
                    var bytesRead = await targetStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;
                        
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);
                    
                    // Send data back to server through MSSQL tunnel
                    var tunnelDataPacket = TunnelProtocol.CreateTunnelDataPacket(connectionId, data);
                    if (_stream != null)
                    {
                        await _stream.WriteAsync(tunnelDataPacket, 0, tunnelDataPacket.Length);
                        Console.WriteLine($"Sent {data.Length} bytes back through tunnel {connectionId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error forwarding tunnel data for connection {connectionId}: {ex.Message}");
            }
            finally
            {
                // Notify server of disconnection
                var disconnectPacket = TunnelProtocol.CreateTunnelDisconnectPacket(connectionId);
                if (_stream != null)
                {
                    try
                    {
                        await _stream.WriteAsync(disconnectPacket, 0, disconnectPacket.Length);
                    }
                    catch { }
                }
                
                _tunnelConnections.TryRemove(connectionId, out _);
                targetClient.Close();
            }
        }        private async Task PerformHandshake()
        {
            if (_stream == null) return;
            
            // Step 1: Send Pre-Login
            if (_options.Verbose)
            {
                Console.WriteLine("Sending Pre-Login packet...");
            }
            var preLoginPacket = TdsProtocol.CreatePreLoginPacket();
            await _stream.WriteAsync(preLoginPacket, 0, preLoginPacket.Length);
            if (_options.Verbose)
            {
                LogTdsPacket(preLoginPacket, "SENT");
            }
            
            // Receive Pre-Login response
            var response = await ReceiveTdsPacket();
            if (response != null)
            {
                if (_options.Verbose)
                {
                    Console.WriteLine("Received Pre-Login response");
                    LogTdsPacket(response, "RECEIVED");
                }
            }
            
            // Step 2: Send Login
            if (_options.Verbose)
            {
                Console.WriteLine("Sending Login packet...");
            }
            var loginPacket = TdsProtocol.CreateLoginPacket(_options.Host, _options.Database, _options.Username, _options.Password);
            await _stream.WriteAsync(loginPacket, 0, loginPacket.Length);
            if (_options.Verbose)
            {
                LogTdsPacket(loginPacket, "SENT");
            }
            
            // Receive Login response
            response = await ReceiveTdsPacket();
            if (response != null)
            {
                if (_options.Verbose)
                {
                    Console.WriteLine("Received Login response");
                    LogTdsPacket(response, "RECEIVED");
                }
                Console.WriteLine("Login successful! Tunnel is now active.");
            }
        }
          private async Task StartInteractiveSession()
        {
            Console.WriteLine("\n=== MSSQL Reverse Tunnel Client ===");
            Console.WriteLine("This client is now connected and ready to handle tunnel requests.");
            Console.WriteLine("The server should have activated a proxy on port 1080.");
            Console.WriteLine("\nYou can also send SQL commands for demonstration:");
            Console.WriteLine("Commands: 'exit' to quit, 'status' for tunnel info, or any SQL command");
            
            while (true)
            {
                Console.Write("\nSQL> ");
                var input = Console.ReadLine();
                
                if (string.IsNullOrWhiteSpace(input))
                    continue;
                    
                if (input.ToLower() == "exit")
                    break;
                    
                if (input.ToLower() == "status")
                {
                    ShowTunnelStatus();
                    continue;
                }
                
                await ExecuteSql(input);
            }
        }
        
        private void ShowTunnelStatus()
        {
            Console.WriteLine("\n=== Tunnel Status ===");
            Console.WriteLine($"Connected to server: {_isConnected}");
            Console.WriteLine($"Active tunnel connections: {_tunnelConnections.Count}");
            
            if (_tunnelConnections.Count > 0)
            {
                Console.WriteLine("Active connections:");
                foreach (var kvp in _tunnelConnections)
                {
                    var client = kvp.Value;
                    var status = client.Connected ? "Connected" : "Disconnected";
                    Console.WriteLine($"  Connection {kvp.Key}: {status}");
                }
            }
            
            Console.WriteLine("Server proxy should be listening on port 1080");
            Console.WriteLine("Example usage:");
            Console.WriteLine("  curl --proxy localhost:1080 http://google.com");
            Console.WriteLine("  proxychains curl http://google.com");
            Console.WriteLine("====================\n");
        }
          private async Task ExecuteSql(string sql)
        {
            try
            {
                Console.WriteLine($"Executing: {sql}");
                
                // Send SQL Batch
                var sqlPacket = TdsProtocol.CreateSqlBatchPacket(sql);
                if (_stream != null)
                {
                    await _stream.WriteAsync(sqlPacket, 0, sqlPacket.Length);
                    LogTdsPacket(sqlPacket, "SENT");
                    
                    Console.WriteLine("SQL query sent (response will be handled by background task)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing SQL: {ex.Message}");
            }
        }
          private async Task<byte[]?> ReceiveTdsPacket()
        {
            try
            {
                if (_stream == null) return null;
                
                var headerBuffer = new byte[8];
                var bytesRead = await _stream.ReadAsync(headerBuffer, 0, 8);
                
                if (bytesRead < 8)
                    return null;
                
                var header = TdsProtocol.ParseTdsHeader(headerBuffer);
                var totalLength = header.Length;
                var remainingBytes = totalLength - 8;
                
                var fullPacket = new byte[totalLength];
                Array.Copy(headerBuffer, 0, fullPacket, 0, 8);
                
                if (remainingBytes > 0)
                {
                    bytesRead = await _stream.ReadAsync(fullPacket, 8, remainingBytes);
                    if (bytesRead < remainingBytes)
                    {
                        Console.WriteLine("Warning: Incomplete packet received");
                    }
                }
                
                return fullPacket;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving packet: {ex.Message}");
                return null;
            }
        }
        
        private void ParseTabularResult(byte[] data)
        {
            if (data.Length < 8)
                return;
                
            var payload = new byte[data.Length - 8];
            Array.Copy(data, 8, payload, 0, payload.Length);
            
            Console.WriteLine("\n--- Query Results ---");
            
            try
            {
                // Simple parsing - look for ROW tokens (0xD1)
                var rowCount = 0;
                for (int i = 0; i < payload.Length; i++)
                {
                    if (payload[i] == 0xD1) // ROW token
                    {
                        rowCount++;
                        Console.WriteLine($"Row {rowCount} found at offset {i}");
                    }
                }
                
                if (rowCount > 0)
                {
                    Console.WriteLine($"Total rows: {rowCount}");
                }
                else
                {
                    Console.WriteLine("No rows returned or command completed successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing result: {ex.Message}");
            }
            
            Console.WriteLine("--- End Results ---\n");
        }
        
        private void LogTdsPacket(byte[] data, string direction)
        {
            if (data.Length >= 8)
            {
                var header = TdsProtocol.ParseTdsHeader(data);
                Console.WriteLine($"{direction} TDS Packet:");
                Console.WriteLine($"  Type: 0x{header.Type:X2} ({GetTdsTypeDescription(header.Type)})");
                Console.WriteLine($"  Status: 0x{header.Status:X2}");
                Console.WriteLine($"  Length: {header.Length}");
                Console.WriteLine($"  SPID: {header.Spid}");
                Console.WriteLine($"  PacketID: {header.PacketId}");
                
                // Show hex dump of first 64 bytes
                var hexDump = BitConverter.ToString(data.Take(Math.Min(64, data.Length)).ToArray()).Replace("-", " ");
                Console.WriteLine($"  Hex: {hexDump}");
                Console.WriteLine();
            }
        }
        
        private string GetTdsTypeDescription(byte type)
        {
            return type switch
            {
                TdsProtocol.SQL_BATCH => "SQL_BATCH",
                TdsProtocol.PRE_TDS7_LOGIN => "PRE_TDS7_LOGIN",
                TdsProtocol.RPC => "RPC",
                TdsProtocol.TABULAR_RESULT => "TABULAR_RESULT",
                TdsProtocol.ATTENTION_SIGNAL => "ATTENTION_SIGNAL",
                TdsProtocol.BULK_LOAD_DATA => "BULK_LOAD_DATA",
                TdsProtocol.FEDERATED_AUTH_TOKEN => "FEDERATED_AUTH_TOKEN",
                TdsProtocol.TRANSACTION_MANAGER => "TRANSACTION_MANAGER",
                TdsProtocol.TDS7_LOGIN => "TDS7_LOGIN",
                TdsProtocol.SSPI => "SSPI",
                TdsProtocol.PRE_LOGIN => "PRE_LOGIN",
                _ => "UNKNOWN"
            };
        }
          public void SendCustomData(byte[] data)
        {
            if (_stream != null && _client != null && _client.Connected)
            {
                _stream.Write(data, 0, data.Length);
                Console.WriteLine($"Sent {data.Length} bytes of custom data");
                LogTdsPacket(data, "SENT CUSTOM");
            }
        }
        
        public async Task<byte[]?> ReceiveCustomData(int expectedLength)
        {
            if (_stream == null || _client == null || !_client.Connected)
                return null;
                
            var buffer = new byte[expectedLength];
            var bytesRead = await _stream.ReadAsync(buffer, 0, expectedLength);
            
            if (bytesRead > 0)
            {
                var result = new byte[bytesRead];
                Array.Copy(buffer, result, bytesRead);
                Console.WriteLine($"Received {bytesRead} bytes of custom data");
                LogTdsPacket(result, "RECEIVED CUSTOM");
                return result;
            }
            
            return null;
        }
    }
}
