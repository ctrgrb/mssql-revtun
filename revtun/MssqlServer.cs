using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Collections.Concurrent;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Collections.Generic;

namespace RevTun
{    public class MssqlServer
    {
#if NETFRAMEWORK
        private TcpListener _listener;
        private TcpListener _proxyListener; // SOCKS proxy listener on port 1080
#else
        private TcpListener? _listener;
        private TcpListener? _proxyListener; // SOCKS proxy listener on port 1080
#endif
        private bool _isRunning;
        private readonly ServerOptions _options;
#if NETFRAMEWORK
        private readonly ConcurrentDictionary<uint, ProxyConnection> _proxyConnections = new ConcurrentDictionary<uint, ProxyConnection>();
        private readonly ConcurrentDictionary<string, MssqlClientHandler> _connectedClients = new ConcurrentDictionary<string, MssqlClientHandler>();
#else
        private readonly ConcurrentDictionary<uint, ProxyConnection> _proxyConnections = new();
        private readonly ConcurrentDictionary<string, MssqlClientHandler> _connectedClients = new();
#endif
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
                {                    var tcpClient = await _listener.AcceptTcpClientAsync();
#if NETFRAMEWORK
                    var clientEndpoint = tcpClient.Client.RemoteEndPoint != null ? tcpClient.Client.RemoteEndPoint.ToString() : "Unknown";
#else
                    var clientEndpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
#endif
                    Console.WriteLine($"MSSQL Client connected: {clientEndpoint}");
                    
                    var clientHandler = new MssqlClientHandler(tcpClient);
                    _connectedClients[clientEndpoint] = clientHandler;
                      // Start proxy listener when first client connects
                    if (_proxyListener == null && _connectedClients.Count == 1)
                    {
                        StartProxyListener();
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
        }        private Task StartProxyListener()
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
                        {                            var proxyClient = await _proxyListener.AcceptTcpClientAsync();                            // Configure TCP socket for optimal performance
                            proxyClient.ReceiveTimeout = 30000; // 30 seconds
                            proxyClient.SendTimeout = 30000; // 30 seconds
                            proxyClient.NoDelay = true; // Critical: Disable Nagle algorithm for immediate data
                            
                            // Configure socket options after connection for maximum performance
                            var socket = proxyClient.Client;
                            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true); // Critical for low latency
                            
                            // Optimize socket buffers for high throughput
                            socket.ReceiveBufferSize = 65536; // 64KB for better throughput
                            socket.SendBufferSize = 65536; // 64KB for better throughput
                            
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
            
            return Task.CompletedTask;
        }private async Task HandleMssqlClientAsync(MssqlClientHandler clientHandler)
        {
            var client = clientHandler.TcpClient;
            var stream = clientHandler.Stream;
            
            try
            {                while (client.Connected && _isRunning)
                {
                    // Read complete TDS packet
                    var packet = await ReceiveTdsPacket(stream);
                    if (packet == null)
                        break;
                    
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"Received {packet.Length} bytes from MSSQL client");
                        LogTdsPacket(packet, "RECEIVED");
                    }
                    
                    // Parse TDS header
                    var header = TdsProtocol.ParseTdsHeader(packet);
                    await ProcessTdsMessage(clientHandler, header, packet);
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
        }        private async Task HandleProxyConnectionAsync(ProxyConnection proxyConnection)
        {
            var buffer = new byte[4096];
            
            try
            {
                // Read initial request
                var bytesRead = await proxyConnection.ProxyStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead < 3)
                {
                    Console.WriteLine($"Invalid request from connection {proxyConnection.Id}");
                    return;
                }
                
                // Detect protocol type
                if (buffer[0] == 0x05) // SOCKS5
                {
                    proxyConnection.IsSocks5 = true;
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
                        
                        Console.WriteLine($"SOCKS5 connection {proxyConnection.Id} requesting: {targetHost}:{targetPort}");
                        
                        // Send connection request to client through MSSQL tunnel
                        var tunnelConnectPacket = TunnelProtocol.CreateTunnelConnectPacket(proxyConnection.Id, targetHost, targetPort);
                        await SendToMssqlClient(tunnelConnectPacket);
                        
                        // Wait for connection acknowledgment - response will be sent in HandleTunnelConnectAck
                        _ = Task.Run(() => ForwardProxyData(proxyConnection));
                    }
                }
                else
                {
                    // Handle HTTP CONNECT requests
                    proxyConnection.IsSocks5 = false;
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
                                
                                // Wait for connection acknowledgment - response will be sent in HandleTunnelConnectAck
                                _ = Task.Run(() => ForwardProxyData(proxyConnection));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling proxy connection {proxyConnection.Id}: {ex.Message}");
                _proxyConnections.TryRemove(proxyConnection.Id, out _);
                proxyConnection.ProxyClient.Close();
            }
        }        private async Task ForwardProxyData(ProxyConnection proxyConnection)
        {            var buffer = new byte[32768]; // Larger 32KB buffer for better throughput
            
            try
            {
                // Configure proxy stream timeouts
                proxyConnection.ProxyStream.ReadTimeout = 30000; // 30 seconds
                proxyConnection.ProxyStream.WriteTimeout = 30000; // 30 seconds
                
                // Wait for connection to be established (shorter timeout for responsiveness)
                var timeout = DateTime.Now.AddSeconds(10);
                while (!proxyConnection.IsActive && DateTime.Now < timeout)
                {
                    await Task.Delay(50); // Shorter delay for faster response
                }
                
                if (!proxyConnection.IsActive)
                {
                    Console.WriteLine($"Timeout waiting for tunnel connection {proxyConnection.Id} to be established");
                    return;
                }
                
                // Now start forwarding data
                while (proxyConnection.ProxyClient.Connected && proxyConnection.IsActive)
                {
                    var bytesRead = await proxyConnection.ProxyStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        if (_options.Verbose)
                        {
                            Console.WriteLine($"Proxy connection {proxyConnection.Id} closed by client");
                        }
                        break;
                    }
                          // Send data directly without unnecessary copying for better performance
                    const int maxDataSize = 32000; // Leave room for tunnel protocol overhead
                    
                    if (bytesRead <= maxDataSize)
                    {
                        // Create packet directly from buffer slice to avoid extra copy
                        var dataSlice = new byte[bytesRead];
                        Array.Copy(buffer, 0, dataSlice, 0, bytesRead);
                        var tunnelDataPacket = TunnelProtocol.CreateTunnelDataPacket(proxyConnection.Id, dataSlice);
                        await SendToMssqlClient(tunnelDataPacket);
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
                            
                            var tunnelDataPacket = TunnelProtocol.CreateTunnelDataPacket(proxyConnection.Id, chunk);
                            await SendToMssqlClient(tunnelDataPacket);
                        }
                    }
                    
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"Forwarded {bytesRead} bytes from proxy to tunnel {proxyConnection.Id}");
                    }}
            }
            catch (System.IO.IOException ioEx) when (ioEx.InnerException is SocketException sockEx)
            {
                // Handle specific socket errors
                if (sockEx.SocketErrorCode == SocketError.ConnectionAborted || 
                    sockEx.SocketErrorCode == SocketError.ConnectionReset)
                {
                    Console.WriteLine($"Proxy connection {proxyConnection.Id} reset by client");
                }
                else
                {
                    Console.WriteLine($"Socket error for proxy connection {proxyConnection.Id}: {sockEx.SocketErrorCode} - {sockEx.Message}");
                }
            }
            catch (ObjectDisposedException)
            {
                Console.WriteLine($"Proxy connection {proxyConnection.Id} was disposed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error forwarding proxy data for connection {proxyConnection.Id}: {ex.Message}");
            }
            finally
            {
                // Send tunnel disconnect
                var disconnectPacket = TunnelProtocol.CreateTunnelDisconnectPacket(proxyConnection.Id);
                await SendToMssqlClient(disconnectPacket);
                
                // Clean up
                _proxyConnections.TryRemove(proxyConnection.Id, out _);
                proxyConnection.ProxyClient.Close();
                
                if (_options.Verbose)
                {
                    Console.WriteLine($"Proxy connection {proxyConnection.Id} closed and cleaned up");
                }
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
        }        private async Task ProcessTdsMessage(MssqlClientHandler clientHandler, TdsHeader header, byte[] data)
        {
            switch (header.Type)
            {
                case TdsProtocol.PRE_LOGIN:
                    await HandlePreLogin(clientHandler, data);
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
        }private async Task HandleTunnelConnectAck(byte[] data)
        {
            try
            {
                var payload = new byte[data.Length - 8];
                Array.Copy(data, 8, payload, 0, payload.Length);
                var connectionId = BitConverter.ToUInt32(payload, 0);
                var success = payload[4] == 1;
                
                if (_proxyConnections.TryGetValue(connectionId, out var proxyConnection))
                {
                    if (success)
                    {
                        // Mark connection as active
                        proxyConnection.IsActive = true;
                        
                        // Send appropriate response based on the detected protocol
                        if (!proxyConnection.ResponseSent)
                        {
                            if (proxyConnection.IsSocks5)
                            {
                                // SOCKS5 success response
                                await proxyConnection.ProxyStream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0, 10);
                                if (_options.Verbose)
                                {
                                    Console.WriteLine($"Sent SOCKS5 success response for {proxyConnection.TargetHost}:{proxyConnection.TargetPort}");
                                }
                            }
                            else
                            {
                                // HTTP CONNECT response
                                var response = "HTTP/1.1 200 Connection established\r\n\r\n";
                                var responseBytes = System.Text.Encoding.ASCII.GetBytes(response);
                                await proxyConnection.ProxyStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                                if (_options.Verbose)
                                {
                                    Console.WriteLine($"Sent HTTP 200 Connection established for {proxyConnection.TargetHost}:{proxyConnection.TargetPort}");
                                }
                            }
                            proxyConnection.ResponseSent = true;
                        }
                        
                        if (_options.Verbose)
                        {
                            Console.WriteLine($"Tunnel connection {connectionId} established successfully to {proxyConnection.TargetHost}:{proxyConnection.TargetPort}");
                        }
                    }
                    else
                    {
                        // Connection failed - send error responses
                        if (proxyConnection.IsSocks5)
                        {
                            // SOCKS5 error response (connection refused)
                            await proxyConnection.ProxyStream.WriteAsync(new byte[] { 0x05, 0x05, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0, 10);
                        }
                        else
                        {
                            // HTTP error response
                            var response = "HTTP/1.1 502 Bad Gateway\r\n\r\n";
                            var responseBytes = System.Text.Encoding.ASCII.GetBytes(response);
                            await proxyConnection.ProxyStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                        }
                        
                        Console.WriteLine($"Tunnel connection {connectionId} failed to {proxyConnection.TargetHost}:{proxyConnection.TargetPort}");
                        
                        // Remove failed connection
                        _proxyConnections.TryRemove(connectionId, out _);
                        proxyConnection.ProxyClient.Close();
                    }
                }
                else
                {
                    Console.WriteLine($"Received tunnel connect ack for unknown connection {connectionId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling tunnel connect ack: {ex.Message}");
            }
        }        private async Task HandleTunnelData(byte[] data)
        {
            try
            {
                var (connectionId, tunnelData) = TunnelProtocol.ParseTunnelDataPacket(data);
                
                if (_proxyConnections.TryGetValue(connectionId, out var proxyConnection) && proxyConnection.ProxyClient.Connected)
                {                    // Write data immediately - don't buffer or parse TLS records
                    // TCP handles fragmentation and reassembly correctly
                    await proxyConnection.ProxyStream.WriteAsync(tunnelData, 0, tunnelData.Length);
                    // Don't flush here - let TCP handle it optimally
                    
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"Forwarded {tunnelData.Length} bytes to proxy connection {connectionId}");
                    }
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
        }        private Task HandleTunnelDisconnect(byte[] data)
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
        }        private async Task HandlePreLogin(MssqlClientHandler clientHandler, byte[] clientPacket)
        {
            Console.WriteLine("Processing Pre-Login request...");
            
            if (_options.Verbose)
            {
                LogTdsPacket(clientPacket, "RECEIVED Pre-Login");
            }
            
            var clientEncryption = TdsProtocol.ParsePreLoginEncryption(clientPacket);
            var serverEncryption = TdsProtocol.ENCRYPT_OFF; // Default to no encryption
            
            // Determine server response based on options and client request
            if (_options.RequireEncryption)
            {
                serverEncryption = TdsProtocol.ENCRYPT_REQ;
            }
            else if (_options.SupportEncryption && (clientEncryption == TdsProtocol.ENCRYPT_ON || clientEncryption == TdsProtocol.ENCRYPT_REQ))
            {
                serverEncryption = TdsProtocol.ENCRYPT_ON;
            }
            
            if (_options.Verbose)
            {
                Console.WriteLine($"Client encryption: {clientEncryption}, Server response: {serverEncryption}");
            }
            
            // Send Pre-Login response with encryption negotiation
            var response = TdsProtocol.CreatePreLoginPacket(serverEncryption);
            await clientHandler.Stream.WriteAsync(response, 0, response.Length);
            
            if (_options.Verbose)
            {
                LogTdsPacket(response, "SENT Pre-Login Response");
            }
            
            // Setup TLS if negotiated and both sides agree
            var useEncryption = (serverEncryption == TdsProtocol.ENCRYPT_ON || serverEncryption == TdsProtocol.ENCRYPT_REQ) &&
                               (clientEncryption == TdsProtocol.ENCRYPT_ON || clientEncryption == TdsProtocol.ENCRYPT_REQ);
            
            if (useEncryption)
            {
                if (_options.Verbose)
                {
                    Console.WriteLine("Starting TLS handshake with client...");
                }
                
                try
                {
                    // Create a self-signed certificate for the server
                    var serverCertificate = CreateSelfSignedCertificate();
                    var sslStream = new SslStream(clientHandler.Stream, false);
                    
                    await sslStream.AuthenticateAsServerAsync(serverCertificate);
                    clientHandler.Stream = sslStream;
                    clientHandler.IsEncrypted = true;
                    
                    if (_options.Verbose)
                    {
                        Console.WriteLine("TLS handshake completed successfully");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"TLS handshake failed: {ex.Message}");
                    if (_options.RequireEncryption)
                    {
                        throw;
                    }
                }
            }
            
            Console.WriteLine($"Pre-Login completed. Encryption: {(clientHandler.IsEncrypted ? "Enabled" : "Disabled")}");
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
            _isRunning = false;            if (_listener != null) _listener.Stop();
            if (_proxyListener != null) _proxyListener.Stop();
            
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
          private async Task<byte[]> ReceiveTdsPacket(Stream stream)
        {
            try
            {
                // Read TDS header (8 bytes) - ensure we get all 8 bytes
                var headerBuffer = new byte[8];
                var totalHeaderRead = 0;
                while (totalHeaderRead < 8)
                {
                    var bytesRead = await stream.ReadAsync(headerBuffer, totalHeaderRead, 8 - totalHeaderRead);
                    if (bytesRead == 0)
                        return new byte[0]; // Connection closed
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
                        var bytesRead = await stream.ReadAsync(fullPacket, 8 + totalDataRead, remainingBytes - totalDataRead);                        if (bytesRead == 0)
                        {
                            Console.WriteLine("Warning: Connection closed while reading packet data");
                            return new byte[0];
                        }
                        totalDataRead += bytesRead;
                    }
                }
                
                return fullPacket;
            }
            catch (Exception ex)
            {                Console.WriteLine($"Error receiving TDS packet: {ex.Message}");
                return new byte[0];
            }
        }
        
        private X509Certificate2 CreateSelfSignedCertificate()
        {
            // For tunnel purposes, create a simple self-signed certificate
            // In production, you would use a proper certificate
            try
            {
                // Try to create a minimal self-signed certificate using RSA
                using (var rsa = System.Security.Cryptography.RSA.Create(2048))
                {
                    var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                        "CN=RevTun-MSSQL-Server", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256,
                        System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                    
                    var cert = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
                    return new X509Certificate2(cert.Export(X509ContentType.Pfx), (string)null, X509KeyStorageFlags.Exportable);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create self-signed certificate: {ex.Message}");
                throw;
            }
        }
    }
}
