using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

namespace RevTun
{    public class MssqlServer
    {
        private TcpListener? _listener;
        private TcpListener? _proxyListener; // SOCKS proxy listener on port 1080
        private bool _isRunning;
        private readonly ServerOptions _options;
        private readonly ConcurrentDictionary<uint, ProxyConnection> _proxyConnections = new();
        private readonly ConcurrentDictionary<string, MssqlClientHandler> _connectedClients = new();
        private uint _nextConnectionId = 1;
        
        public MssqlServer(ServerOptions options)
        {
            _options = options;
        }        public async Task StartAsync()
        {
            var bindAddress = IPAddress.Parse(_options.BindAddress);
            _listener = new TcpListener(bindAddress, _options.Port);
            _listener.Start();
            _isRunning = true;
            
            Console.WriteLine($"MSSQL Server started on {_options.BindAddress}:{_options.Port}");
            Console.WriteLine("Waiting for client connections...");
            
            // Start proxy listener (will be activated when a client connects)
            Console.WriteLine($"Proxy service ready on port {_options.ProxyPort} (will activate when client connects)");
            
            while (_isRunning)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    Console.WriteLine($"MSSQL Client connected: {clientEndpoint}");
                    
                    var clientHandler = new MssqlClientHandler(tcpClient);
                    _connectedClients[clientEndpoint] = clientHandler;
                    
                    // Start proxy listener when first client connects
                    if (_proxyListener == null && _connectedClients.Count == 1)
                    {
                        await StartProxyListener();
                    }
                    
                    // Handle client in a separate task
                    _ = Task.Run(() => HandleMssqlClientAsync(clientHandler));
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Console.WriteLine($"Error accepting MSSQL client: {ex.Message}");
                    }
                }
            }
        }
          private async Task StartProxyListener()
        {
            try
            {
                var bindAddress = IPAddress.Parse(_options.BindAddress);
                _proxyListener = new TcpListener(bindAddress, _options.ProxyPort);
                _proxyListener.Start();
                Console.WriteLine($"✓ Proxy tunnel activated on {_options.BindAddress}:{_options.ProxyPort}");
                
                // Start accepting proxy connections
                _ = Task.Run(async () =>
                {
                    while (_isRunning && _proxyListener != null)
                    {
                        try
                        {
                            var proxyClient = await _proxyListener.AcceptTcpClientAsync();
                            var connectionId = _nextConnectionId++;
                            var proxyConnection = new ProxyConnection(connectionId, proxyClient);
                            _proxyConnections[connectionId] = proxyConnection;
                            
                            if (_options.Verbose)
                            {
                                Console.WriteLine($"Proxy connection {connectionId} established from {proxyClient.Client.RemoteEndPoint}");
                            }
                            
                            // Handle proxy connection
                            _ = Task.Run(() => HandleProxyConnectionAsync(proxyConnection));
                        }
                        catch (Exception ex)
                        {
                            if (_isRunning)
                            {
                                Console.WriteLine($"Error accepting proxy connection: {ex.Message}");
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting proxy listener: {ex.Message}");
            }
        }
          private async Task HandleMssqlClientAsync(MssqlClientHandler clientHandler)
        {
            var buffer = new byte[4096];
            var client = clientHandler.TcpClient;
            var stream = clientHandler.Stream;
            
            try
            {
                while (client.Connected && _isRunning)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;
                          var receivedData = new byte[bytesRead];
                    Array.Copy(buffer, receivedData, bytesRead);
                    
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"Received {bytesRead} bytes from MSSQL client");
                        LogTdsPacket(receivedData, "RECEIVED");
                    }
                    
                    // Parse TDS header
                    if (bytesRead >= 8)
                    {
                        var header = TdsProtocol.ParseTdsHeader(receivedData);
                        await ProcessTdsMessage(clientHandler, header, receivedData);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling MSSQL client: {ex.Message}");
            }
            finally
            {
                // Remove client from connected clients
                _connectedClients.TryRemove(clientHandler.ClientEndpoint, out _);
                
                // Stop proxy listener if no clients are connected
                if (_connectedClients.IsEmpty && _proxyListener != null)
                {
                    _proxyListener.Stop();
                    _proxyListener = null;
                    Console.WriteLine("Proxy tunnel deactivated (no clients connected)");
                }
                
                client.Close();
                Console.WriteLine($"MSSQL Client {clientHandler.ClientEndpoint} disconnected");
            }
        }
        
        private async Task HandleProxyConnectionAsync(ProxyConnection proxyConnection)
        {
            var buffer = new byte[4096];
            
            try
            {
                // Read SOCKS5 handshake
                var bytesRead = await proxyConnection.ProxyStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead < 3)
                {
                    Console.WriteLine($"Invalid SOCKS5 handshake from connection {proxyConnection.Id}");
                    return;
                }
                
                // Simple SOCKS5 handshake - respond with no authentication required
                if (buffer[0] == 0x05) // SOCKS5
                {
                    await proxyConnection.ProxyStream.WriteAsync(new byte[] { 0x05, 0x00 }, 0, 2);
                    
                    // Read connection request
                    bytesRead = await proxyConnection.ProxyStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead >= 10 && buffer[0] == 0x05 && buffer[1] == 0x01) // CONNECT command
                    {
                        string targetHost;
                        int targetPort;
                        
                        if (buffer[3] == 0x01) // IPv4
                        {
                            targetHost = $"{buffer[4]}.{buffer[5]}.{buffer[6]}.{buffer[7]}";
                            targetPort = (buffer[8] << 8) | buffer[9];
                        }
                        else if (buffer[3] == 0x03) // Domain name
                        {
                            var domainLength = buffer[4];
                            targetHost = System.Text.Encoding.ASCII.GetString(buffer, 5, domainLength);
                            targetPort = (buffer[5 + domainLength] << 8) | buffer[6 + domainLength];
                        }
                        else
                        {
                            // Send error response
                            await proxyConnection.ProxyStream.WriteAsync(new byte[] { 0x05, 0x08, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0, 10);
                            return;
                        }
                        
                        proxyConnection.TargetHost = targetHost;
                        proxyConnection.TargetPort = targetPort;
                        
                        Console.WriteLine($"Proxy connection {proxyConnection.Id} requesting: {targetHost}:{targetPort}");
                        
                        // Send connection request to client through MSSQL tunnel
                        var tunnelConnectPacket = TunnelProtocol.CreateTunnelConnectPacket(proxyConnection.Id, targetHost, targetPort);
                        await SendToMssqlClient(tunnelConnectPacket);
                        
                        // Send SOCKS5 success response
                        await proxyConnection.ProxyStream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0, 10);
                        
                        // Start forwarding data
                        await ForwardProxyData(proxyConnection);
                    }
                }
                else
                {
                    // Handle direct HTTP proxy requests
                    var request = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    if (request.StartsWith("CONNECT "))
                    {
                        var parts = request.Split(' ');
                        if (parts.Length >= 2)
                        {
                            var hostPort = parts[1].Split(':');
                            if (hostPort.Length == 2)
                            {
                                proxyConnection.TargetHost = hostPort[0];
                                proxyConnection.TargetPort = int.Parse(hostPort[1]);
                                
                                Console.WriteLine($"HTTP CONNECT request from connection {proxyConnection.Id}: {proxyConnection.TargetHost}:{proxyConnection.TargetPort}");
                                
                                // Send connection request to client
                                var tunnelConnectPacket = TunnelProtocol.CreateTunnelConnectPacket(proxyConnection.Id, proxyConnection.TargetHost, proxyConnection.TargetPort);
                                await SendToMssqlClient(tunnelConnectPacket);
                                
                                // Send HTTP 200 Connection established
                                var response = "HTTP/1.1 200 Connection established\r\n\r\n";
                                var responseBytes = System.Text.Encoding.ASCII.GetBytes(response);
                                await proxyConnection.ProxyStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                                
                                // Start forwarding data
                                await ForwardProxyData(proxyConnection);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling proxy connection {proxyConnection.Id}: {ex.Message}");
            }
            finally
            {
                _proxyConnections.TryRemove(proxyConnection.Id, out _);
                proxyConnection.ProxyClient.Close();
                Console.WriteLine($"Proxy connection {proxyConnection.Id} closed");
            }
        }
        
        private async Task ForwardProxyData(ProxyConnection proxyConnection)
        {
            var buffer = new byte[4096];
            
            try
            {
                while (proxyConnection.ProxyClient.Connected && proxyConnection.IsActive)
                {
                    var bytesRead = await proxyConnection.ProxyStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                        break;
                        
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);
                    
                    // Send data through MSSQL tunnel
                    var tunnelDataPacket = TunnelProtocol.CreateTunnelDataPacket(proxyConnection.Id, data);
                    await SendToMssqlClient(tunnelDataPacket);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error forwarding proxy data for connection {proxyConnection.Id}: {ex.Message}");
            }
        }
        
        private async Task SendToMssqlClient(byte[] packet)
        {
            // Send to the first available client (in a real implementation, you might want load balancing)
            var client = _connectedClients.Values.FirstOrDefault(c => c.IsAuthenticated);
            if (client != null)
            {
                try
                {
                    await client.Stream.WriteAsync(packet, 0, packet.Length);
                    LogTdsPacket(packet, "SENT TUNNEL");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending tunnel packet to client: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("No authenticated MSSQL client available for tunneling");
            }
        }
          private async Task ProcessTdsMessage(MssqlClientHandler clientHandler, TdsHeader header, byte[] data)
        {
            switch (header.Type)
            {
                case TdsProtocol.PRE_LOGIN:
                    await HandlePreLogin(clientHandler);
                    break;
                    
                case TdsProtocol.TDS7_LOGIN:
                    await HandleLogin(clientHandler);
                    break;
                    
                case TdsProtocol.SQL_BATCH:
                    await HandleSqlBatch(clientHandler, data);
                    break;
                    
                // Handle tunnel protocol messages
                case TunnelProtocol.TUNNEL_CONNECT_ACK:
                    await HandleTunnelConnectAck(data);
                    break;
                    
                case TunnelProtocol.TUNNEL_DATA:
                    await HandleTunnelData(data);
                    break;
                    
                case TunnelProtocol.TUNNEL_DISCONNECT:
                    await HandleTunnelDisconnect(data);
                    break;
                    
                default:
                    Console.WriteLine($"Unhandled TDS message type: 0x{header.Type:X2}");
                    break;
            }
        }
          private Task HandleTunnelConnectAck(byte[] data)
        {
            // This would be handled by the client, not the server
            Console.WriteLine("Received tunnel connect ack (unexpected on server)");
            return Task.CompletedTask;
        }
        
        private async Task HandleTunnelData(byte[] data)
        {
            try
            {
                var (connectionId, tunnelData) = TunnelProtocol.ParseTunnelDataPacket(data);
                
                if (_proxyConnections.TryGetValue(connectionId, out var proxyConnection))
                {
                    await proxyConnection.ProxyStream.WriteAsync(tunnelData, 0, tunnelData.Length);
                    Console.WriteLine($"Forwarded {tunnelData.Length} bytes to proxy connection {connectionId}");
                }
                else
                {
                    Console.WriteLine($"Tunnel data received for unknown connection {connectionId}");
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
                
                if (_proxyConnections.TryRemove(connectionId, out var proxyConnection))
                {
                    proxyConnection.IsActive = false;
                    proxyConnection.ProxyClient.Close();
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"Tunnel connection {connectionId} disconnected by client");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling tunnel disconnect: {ex.Message}");
            }
            return Task.CompletedTask;
        }
          private async Task HandlePreLogin(MssqlClientHandler clientHandler)
        {
            Console.WriteLine("Processing Pre-Login request...");
            
            // Send Pre-Login response
            var response = TdsProtocol.CreatePreLoginPacket();
            await clientHandler.Stream.WriteAsync(response, 0, response.Length);
            
            LogTdsPacket(response, "SENT");
            Console.WriteLine("Pre-Login response sent");
        }
        
        private async Task HandleLogin(MssqlClientHandler clientHandler)
        {
            Console.WriteLine("Processing Login request...");
            
            // Send Login acknowledgment (simplified)
            var loginAck = CreateLoginAckPacket();
            await clientHandler.Stream.WriteAsync(loginAck, 0, loginAck.Length);
            
            // Mark client as authenticated
            clientHandler.IsAuthenticated = true;
            
            LogTdsPacket(loginAck, "SENT");
            Console.WriteLine($"Login acknowledgment sent - Client {clientHandler.ClientEndpoint} authenticated");
        }
        
        private async Task HandleSqlBatch(MssqlClientHandler clientHandler, byte[] data)
        {
            // Extract SQL from the packet (skip 8-byte header)
            var sqlBytes = new byte[data.Length - 8];
            Array.Copy(data, 8, sqlBytes, 0, sqlBytes.Length);
            var sql = Encoding.Unicode.GetString(sqlBytes).TrimEnd('\0');
            
            Console.WriteLine($"Executing SQL: {sql}");
            
            // Generate sample response data
            var columnNames = new[] { "ID", "Name", "Value" };
            var rows = new[]
            {
                new[] { "1", "Sample Row 1", "100" },
                new[] { "2", "Sample Row 2", "200" },
                new[] { "3", "Sample Row 3", "300" }
            };
            
            // Send tabular result
            var resultPacket = TdsProtocol.CreateTabularResultPacket(rows, columnNames);
            await clientHandler.Stream.WriteAsync(resultPacket, 0, resultPacket.Length);
            
            LogTdsPacket(resultPacket, "SENT");
            Console.WriteLine("Query result sent");
        }
        
        private byte[] CreateLoginAckPacket()
        {
            var payload = new List<byte>();
            
            // Token: LOGINACK (0xAD)
            payload.Add(0xAD);
            
            // Interface (1 byte) - SQL_DFLT
            payload.Add(0x01);
            
            // TDS Version (4 bytes)
            payload.AddRange(BitConverter.GetBytes((uint)0x74000004));
            
            // Program name
            var programName = "Microsoft SQL Server";
            payload.Add((byte)programName.Length);
            payload.AddRange(Encoding.Unicode.GetBytes(programName));
            
            // Server version (4 bytes) - SQL Server 2019
            payload.AddRange(new byte[] { 0x0F, 0x00, 0x0A, 0x40 });
            
            // Token: DONE (0xFD)
            payload.Add(0xFD);
            payload.AddRange(BitConverter.GetBytes((ushort)0x0000)); // Status
            payload.AddRange(BitConverter.GetBytes((ushort)0x0000)); // CurCmd
            payload.AddRange(BitConverter.GetBytes((uint)0x00000000)); // RowCount
            
            var totalLength = (ushort)(8 + payload.Count);
            var header = TdsProtocol.CreateTdsHeader(TdsProtocol.TABULAR_RESULT, TdsProtocol.STATUS_EOM, totalLength, 0, 1, 0);
            
            var packet = new byte[totalLength];
            Array.Copy(header, 0, packet, 0, 8);
            Array.Copy(payload.ToArray(), 0, packet, 8, payload.Count);
            
            return packet;
        }
        
        private void LogTdsPacket(byte[] data, string direction)
        {
            if (data.Length >= 8)
            {
                var header = TdsProtocol.ParseTdsHeader(data);
                Console.WriteLine($"{direction} TDS Packet:");
                Console.WriteLine($"  Type: 0x{header.Type:X2}");
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
          public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            _proxyListener?.Stop();
            
            // Close all proxy connections
            foreach (var connection in _proxyConnections.Values)
            {
                connection.IsActive = false;
                connection.ProxyClient.Close();
            }
            _proxyConnections.Clear();
            
            // Close all MSSQL client connections
            foreach (var client in _connectedClients.Values)
            {
                client.TcpClient.Close();
            }
            _connectedClients.Clear();
            
            Console.WriteLine("Server stopped");
        }
    }
}
