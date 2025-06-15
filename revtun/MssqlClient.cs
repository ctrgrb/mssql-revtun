using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Collections.Concurrent;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Linq;

namespace RevTun
{    public class MssqlClient
    {
#if NETFRAMEWORK
        private TcpClient _client;
        private Stream _stream;
#else
        private TcpClient? _client;
        private Stream? _stream;
#endif
        private readonly ClientOptions _options;
#if NETFRAMEWORK
        private readonly ConcurrentDictionary<uint, TcpClient> _tunnelConnections = new ConcurrentDictionary<uint, TcpClient>();
#else
        private readonly ConcurrentDictionary<uint, TcpClient> _tunnelConnections = new();
#endif
        private bool _isConnected = false;
        
        public MssqlClient(ClientOptions options)
        {
            _options = options;
        }        public async Task ConnectAsync()
        {
            try
            {
                if (_options.Debug)
                    Console.WriteLine($"Connecting to MSSQL Server at {_options.Host}:{_options.Port}...");
                
                _client = new TcpClient();
                await _client.ConnectAsync(_options.Host, _options.Port);
                _stream = _client.GetStream();
                _isConnected = true;
                
                if (_options.Debug)
                    Console.WriteLine("Connected to server!");
                
                // Perform TDS handshake
                await PerformHandshake();
                
                // Start background task to handle tunnel messages
                _ = Task.Run(HandleTunnelMessages);
                
                // Keep connection alive - no interactive session
                if (!_options.AutoExit)
                {
                    if (_options.Debug)
                        Console.WriteLine("Tunnel established. Press Ctrl+C to exit.");
                    
                    // Keep running until interrupted
                    while (_isConnected)
                    {
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    if (_options.Debug)
                        Console.WriteLine("Auto-exit mode: Connection test completed successfully.");
                    // Wait a bit to ensure tunnel is established
                    await Task.Delay(2000);
                }
            }            catch (Exception ex)
            {
                // Always show connection errors, even in silent mode
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
                                if (_options.Debug)
                                {
                                    Console.WriteLine("Query executed successfully!");
                                    LogTdsPacket(packet, "RECEIVED");
                                    ParseTabularResult(packet);
                                }
                                break;
                                
                            default:
                                if (_options.Debug)
                                {
                                    Console.WriteLine($"Received TDS message type: 0x{header.Type:X2}");
                                    LogTdsPacket(packet, "RECEIVED");
                                }
                                break;
                        }
                    }
                }            }
            catch (Exception ex)
            {
                if (_options.Debug)
                    Console.WriteLine($"Error handling tunnel messages: {ex.Message}");
            }
        }        private async Task HandleTunnelConnect(byte[] data)
        {            try
            {
                var (connectionId, host, port) = TunnelProtocol.ParseTunnelConnectPacket(data);
                if (_options.Debug)
                    Console.WriteLine($"Tunnel connect request: {connectionId} -> {host}:{port}");
                
                var targetClient = new TcpClient();
                bool connected = false;
                string errorMessage = "";
                  try
                {                    // Configure TCP socket for optimal performance
                    targetClient.ReceiveTimeout = 30000; // 30 seconds
                    targetClient.SendTimeout = 30000; // 30 seconds
                    targetClient.NoDelay = true; // Critical: Disable Nagle algorithm for immediate data
                    
                    await targetClient.ConnectAsync(host, port);
                      // Configure socket options after connection for maximum performance
                    var socket = targetClient.Client;
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true); // Critical for low latency
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); // Allow port reuse
                    
                    // Optimize socket buffers for high throughput
                    socket.ReceiveBufferSize = 65536; // 64KB for better throughput
                    socket.SendBufferSize = 65536; // 64KB for better throughput
                      connected = true;
                    _tunnelConnections[connectionId] = targetClient;
                    
                    if (_options.Debug)
                        Console.WriteLine($"Successfully connected to {host}:{port} for tunnel {connectionId}");
                    
                    // Start forwarding data from target back to server
                    _ = Task.Run(() => ForwardTunnelData(connectionId, targetClient));
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    if (_options.Debug)
                        Console.WriteLine($"Failed to connect to {host}:{port}: {ex.Message}");
                    targetClient.Close();
                }
                
                // Send acknowledgment back to server
                var ackPacket = TunnelProtocol.CreateTunnelConnectAckPacket(connectionId, connected, errorMessage);                if (_stream != null)
                {
                    await _stream.WriteAsync(ackPacket, 0, ackPacket.Length);
                    if (_options.Debug)
                        LogTdsPacket(ackPacket, "SENT TUNNEL ACK");
                }
            }
            catch (Exception ex)
            {
                if (_options.Debug)
                    Console.WriteLine($"Error handling tunnel connect: {ex.Message}");
            }
        }        private async Task HandleTunnelData(byte[] data)
        {
            try
            {
                var (connectionId, tunnelData) = TunnelProtocol.ParseTunnelDataPacket(data);
                
                if (_tunnelConnections.TryGetValue(connectionId, out var targetClient) && targetClient.Connected)
                {
                    var targetStream = targetClient.GetStream();                    // Write data immediately - don't buffer or parse TLS records
                    // TCP handles fragmentation and reassembly correctly
                    await targetStream.WriteAsync(tunnelData, 0, tunnelData.Length);
                    // Don't flush here - let TCP handle it optimally
                    
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"Forwarded {tunnelData.Length} bytes to target for connection {connectionId}");
                    }
                }                else
                {
                    if (_options.Debug)
                        Console.WriteLine($"Tunnel data received for unknown/closed connection {connectionId}");
                }
            }
            catch (Exception ex)
            {
                if (_options.Debug)
                    Console.WriteLine($"Error handling tunnel data: {ex.Message}");
            }
        }        private Task HandleTunnelDisconnect(byte[] data)
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
                }            }
            catch (Exception ex)
            {
                if (_options.Debug)
                    Console.WriteLine($"Error handling tunnel disconnect: {ex.Message}");
            }
            return Task.CompletedTask;
        }        private async Task ForwardTunnelData(uint connectionId, TcpClient targetClient)
        {
            var buffer = new byte[32768]; // Larger 32KB buffer for better throughput
            var targetStream = targetClient.GetStream();
            
            try
            {
                // Set stream timeouts
                targetStream.ReadTimeout = 30000; // 30 seconds
                targetStream.WriteTimeout = 30000; // 30 seconds
                
                while (targetClient.Connected && _isConnected)
                {
                    var bytesRead = await targetStream.ReadAsync(buffer, 0, buffer.Length);                    if (bytesRead == 0)
                    {
                        // Connection closed by remote
                        if (_options.Debug)
                        {
                            Console.WriteLine($"Target connection {connectionId} closed by remote");
                        }
                        break;
                    }
                          // Send data directly without unnecessary copying for small packets
                    const int maxDataSize = 32000; // Leave room for tunnel protocol overhead
                    
                    if (bytesRead <= maxDataSize)
                    {
                        // Create packet directly from buffer slice to avoid copy
                        var dataSlice = new byte[bytesRead];
                        Array.Copy(buffer, 0, dataSlice, 0, bytesRead);
                        var tunnelDataPacket = TunnelProtocol.CreateTunnelDataPacket(connectionId, dataSlice);
                        
                        if (_stream != null)
                        {
                            await _stream.WriteAsync(tunnelDataPacket, 0, tunnelDataPacket.Length);                        }
                        else
                        {
                            if (_options.Debug)
                                Console.WriteLine($"Main MSSQL connection lost, closing tunnel {connectionId}");
                            break;
                        }
                    }
                    else
                    {
                        // Handle large packets (rare case)
                        var data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);
                        
                        for (int offset = 0; offset < data.Length; offset += maxDataSize)
                        {
                            var chunkSize = Math.Min(maxDataSize, data.Length - offset);
                            var chunk = new byte[chunkSize];
                            Array.Copy(data, offset, chunk, 0, chunkSize);
                            
                            var tunnelDataPacket = TunnelProtocol.CreateTunnelDataPacket(connectionId, chunk);
                            if (_stream != null)
                            {
                                await _stream.WriteAsync(tunnelDataPacket, 0, tunnelDataPacket.Length);                            }
                            else
                            {
                                if (_options.Debug)
                                    Console.WriteLine($"Main MSSQL connection lost, closing tunnel {connectionId}");
                                return;
                            }
                        }
                    }                      if (_options.Debug)
                    {
                        Console.WriteLine($"Sent {bytesRead} bytes back through tunnel {connectionId}");
                    }
                }
            }            catch (System.IO.IOException ioEx) when (ioEx.InnerException is SocketException sockEx)
            {
                // Handle specific socket errors
                if (sockEx.SocketErrorCode == SocketError.ConnectionAborted || 
                    sockEx.SocketErrorCode == SocketError.ConnectionReset)
                {
                    if (_options.Debug)
                        Console.WriteLine($"Target connection {connectionId} reset by peer");
                }
                else
                {
                    if (_options.Debug)
                        Console.WriteLine($"Socket error for connection {connectionId}: {sockEx.SocketErrorCode} - {sockEx.Message}");
                }
            }
            catch (ObjectDisposedException)
            {
                if (_options.Debug)
                    Console.WriteLine($"Connection {connectionId} was disposed");
            }
            catch (Exception ex)
            {
                if (_options.Debug)
                    Console.WriteLine($"Error forwarding tunnel data for connection {connectionId}: {ex.Message}");
            }
            finally
            {
                // Clean up connection
                if (_tunnelConnections.TryRemove(connectionId, out var removedClient))
                {
                    try
                    {
                        removedClient.Close();
                    }
                    catch { }
                }
                
                // Send tunnel disconnect notification
                if (_stream != null)
                {
                    try
                    {
                        var disconnectPacket = TunnelProtocol.CreateTunnelDisconnectPacket(connectionId);
                        await _stream.WriteAsync(disconnectPacket, 0, disconnectPacket.Length);
                        Console.WriteLine($"Sent disconnect for tunnel {connectionId}");
                    }
                    catch { }
                }
            }
        }        private async Task PerformHandshake()
        {
            if (_stream == null) return;
            
            // Step 1: Send Pre-Login with encryption settings
            if (_options.Verbose)
            {
                Console.WriteLine("Sending Pre-Login packet...");
            }
            
            var encryptionOption = _options.RequireEncryption ? TdsProtocol.ENCRYPT_REQ : 
                                  _options.RequestEncryption ? TdsProtocol.ENCRYPT_ON : 
                                  TdsProtocol.ENCRYPT_OFF;
            
            var preLoginPacket = TdsProtocol.CreatePreLoginPacket(encryptionOption);
            await _stream.WriteAsync(preLoginPacket, 0, preLoginPacket.Length);
            if (_options.Verbose)
            {
                LogTdsPacket(preLoginPacket, "SENT");
            }
            
            // Receive Pre-Login response
            var response = await ReceiveTdsPacket();
            if (response == null)
            {
                throw new InvalidOperationException("No response received to Pre-Login packet");
            }
            
            if (_options.Verbose)
            {
                Console.WriteLine("Received Pre-Login response");
                LogTdsPacket(response, "RECEIVED");
            }
            
            // Parse the server's encryption response
            var serverEncryption = TdsProtocol.ParsePreLoginEncryption(response);
            var useEncryption = false;
            
            switch (serverEncryption)
            {
                case TdsProtocol.ENCRYPT_ON:
                case TdsProtocol.ENCRYPT_REQ:
                    useEncryption = true;
                    if (_options.Verbose)
                    {
                        Console.WriteLine("Server supports/requires encryption - enabling TLS");
                    }
                    break;
                case TdsProtocol.ENCRYPT_OFF:
                    if (_options.RequireEncryption)
                    {
                        throw new InvalidOperationException("Server does not support encryption but client requires it");
                    }
                    if (_options.Verbose)
                    {
                        Console.WriteLine("Server does not support encryption - using plaintext");
                    }
                    break;
                default:
                    if (_options.RequireEncryption)
                    {
                        throw new InvalidOperationException("Server encryption response unclear but client requires encryption");
                    }
                    break;
            }
            
            // Step 1.5: Setup TLS if negotiated
            if (useEncryption)
            {
                if (_options.Verbose)
                {
                    Console.WriteLine("Starting TLS handshake...");
                }
                
                var sslStream = new SslStream(_stream, false, ValidateServerCertificate);
                try
                {
                    await sslStream.AuthenticateAsClientAsync(_options.Host);
                    _stream = sslStream;
                    if (_options.Verbose)
                    {
                        Console.WriteLine("TLS handshake completed successfully");
                    }
                }
                catch (Exception ex)
                {
                    if (_options.RequireEncryption)
                    {
                        throw new InvalidOperationException($"TLS handshake failed: {ex.Message}");
                    }
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"TLS handshake failed, continuing with plaintext: {ex.Message}");
                    }
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
            if (response != null)            {
                if (_options.Verbose)
                {
                    Console.WriteLine("Received Login response");
                    LogTdsPacket(response, "RECEIVED");
                }
                if (_options.Debug)
                {
                    Console.WriteLine($"Login successful! Tunnel is now active. {(useEncryption ? "(Encrypted)" : "(Plaintext)")}");
                }
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
            }        }        
#if NETFRAMEWORK
        private async Task<byte[]> ReceiveTdsPacket()
        {
            try
            {
                if (_stream == null) return new byte[0];
#else
        private async Task<byte[]?> ReceiveTdsPacket()
        {
            try
            {
                if (_stream == null) return null;
#endif
                
                // Read TDS header (8 bytes) - ensure we get all 8 bytes
                var headerBuffer = new byte[8];
                var totalHeaderRead = 0;
                while (totalHeaderRead < 8)
                {
                    var bytesRead = await _stream.ReadAsync(headerBuffer, totalHeaderRead, 8 - totalHeaderRead);
                    if (bytesRead == 0)
                        
#if NETFRAMEWORK
                        return new byte[0]; // Connection closed
#else
                        return null; // Connection closed
#endif
                    totalHeaderRead += bytesRead;
                }
                
                var header = TdsProtocol.ParseTdsHeader(headerBuffer);
                var totalLength = header.Length;
                var remainingBytes = totalLength - 8;
                
                var fullPacket = new byte[totalLength];
                Array.Copy(headerBuffer, 0, fullPacket, 0, 8);
                
                // Read remaining bytes - ensure we get ALL remaining bytes
                if (remainingBytes > 0)
                {
                    var totalDataRead = 0;
                    while (totalDataRead < remainingBytes)
                    {
                        var bytesRead = await _stream.ReadAsync(fullPacket, 8 + totalDataRead, remainingBytes - totalDataRead);
                        if (bytesRead == 0)
                        {
                            Console.WriteLine("Warning: Connection closed while reading packet data");
                            
#if NETFRAMEWORK
                            return new byte[0];
#else
                            return null;
#endif
                        }
                        totalDataRead += bytesRead;
                    }
                }
                
                return fullPacket;            }            catch (Exception ex)
            {
                if (_options.Debug)
                    Console.WriteLine($"Error receiving packet: {ex.Message}");
#if NETFRAMEWORK
                return new byte[0];
#else
                return null;
#endif
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
            switch (type)
            {
                case TdsProtocol.SQL_BATCH:
                    return "SQL_BATCH";
                case TdsProtocol.PRE_TDS7_LOGIN:
                    return "PRE_TDS7_LOGIN";
                case TdsProtocol.RPC:
                    return "RPC";
                case TdsProtocol.TABULAR_RESULT:
                    return "TABULAR_RESULT";
                case TdsProtocol.ATTENTION_SIGNAL:
                    return "ATTENTION_SIGNAL";
                case TdsProtocol.BULK_LOAD_DATA:
                    return "BULK_LOAD_DATA";
                case TdsProtocol.FEDERATED_AUTH_TOKEN:
                    return "FEDERATED_AUTH_TOKEN";
                case TdsProtocol.TRANSACTION_MANAGER:
                    return "TRANSACTION_MANAGER";
                case TdsProtocol.TDS7_LOGIN:
                    return "TDS7_LOGIN";
                case TdsProtocol.SSPI:
                    return "SSPI";
                case TdsProtocol.PRE_LOGIN:
                    return "PRE_LOGIN";
                default:
                    return "UNKNOWN";
            }
        }
          public void SendCustomData(byte[] data)
        {
            if (_stream != null && _client != null && _client.Connected)
            {
                _stream.Write(data, 0, data.Length);
                Console.WriteLine($"Sent {data.Length} bytes of custom data");
                LogTdsPacket(data, "SENT CUSTOM");
            }        }          
#if NETFRAMEWORK
        public async Task<byte[]> ReceiveCustomData(int expectedLength)
        {
            if (_stream == null || _client == null || !_client.Connected)
                return new byte[0];
#else
        public async Task<byte[]?> ReceiveCustomData(int expectedLength)
        {
            if (_stream == null || _client == null || !_client.Connected)
                return null;
#endif
                
            var buffer = new byte[expectedLength];
            var bytesRead = await _stream.ReadAsync(buffer, 0, expectedLength);
            
            if (bytesRead > 0)
            {
                var result = new byte[bytesRead];
                Array.Copy(buffer, result, bytesRead);
                Console.WriteLine($"Received {bytesRead} bytes of custom data");
                LogTdsPacket(result, "RECEIVED CUSTOM");
                return result;            }            
#if NETFRAMEWORK
            return new byte[0];
#else
            return null;
#endif
        }
        
#if NETFRAMEWORK
        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
#else
        private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
#endif
        {
            // For tunnel purposes, we accept any certificate
            // In production, you might want to implement proper certificate validation
            if (_options.Verbose && sslPolicyErrors != SslPolicyErrors.None)
            {
                Console.WriteLine($"TLS Certificate validation warnings: {sslPolicyErrors}");
            }
            return true;
        }
    }
}
