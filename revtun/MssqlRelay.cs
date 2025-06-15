using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Linq;

namespace RevTun
{
    public class MssqlRelay
    {
#if NETFRAMEWORK
        private TcpListener _listener;
        private readonly ConcurrentDictionary<string, RelayConnection> _connections = new ConcurrentDictionary<string, RelayConnection>();
#else
        private TcpListener? _listener;
        private readonly ConcurrentDictionary<string, RelayConnection> _connections = new();
#endif
        private bool _isRunning;
        private readonly RelayOptions _options;
        
        public MssqlRelay(RelayOptions options)
        {
            _options = options;
        }

        public async Task StartAsync()
        {
            var bindAddress = IPAddress.Parse(_options.BindAddress);
            _listener = new TcpListener(bindAddress, _options.Port);
            _listener.Start();
            _isRunning = true;
            
            Console.WriteLine($"MSSQL Relay started on {_options.BindAddress}:{_options.Port}");
            Console.WriteLine($"Forwarding to server: {_options.ServerHost}:{_options.ServerPort}");
            Console.WriteLine("Waiting for client connections...");
            
            while (_isRunning)
            {
                try
                {
#if NETFRAMEWORK
                    var clientSocket = await _listener.AcceptTcpClientAsync();
#else
                    var clientSocket = await _listener.AcceptTcpClientAsync();
#endif
                    var clientEndpoint = clientSocket.Client.RemoteEndPoint?.ToString() ?? "Unknown";
                    
                    if (_options.Verbose)
                    {
                        Console.WriteLine($"New client connection from {clientEndpoint}");
                    }
                    
                    // Handle each client connection in a separate task
                    _ = Task.Run(() => HandleClientConnectionAsync(clientSocket, clientEndpoint));
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Console.WriteLine($"Error accepting client: {ex.Message}");
                    }
                }
            }
        }        private async Task HandleClientConnectionAsync(TcpClient clientSocket, string clientEndpoint)
        {
#if NETFRAMEWORK
            TcpClient serverSocket = null;
#else
            TcpClient? serverSocket = null;
#endif
            try
            {
                // Connect to the actual server
                serverSocket = new TcpClient();
                await serverSocket.ConnectAsync(_options.ServerHost, _options.ServerPort);
                
                if (_options.Verbose)
                {
                    Console.WriteLine($"Established relay connection for {clientEndpoint} -> {_options.ServerHost}:{_options.ServerPort}");
                }
                
                var relayConnection = new RelayConnection(clientSocket, serverSocket, clientEndpoint);
                _connections[clientEndpoint] = relayConnection;
                
                // Start bidirectional data forwarding
                var clientToServerTask = ForwardDataAsync(relayConnection.ClientStream, relayConnection.ServerStream, "Client->Server", clientEndpoint);
                var serverToClientTask = ForwardDataAsync(relayConnection.ServerStream, relayConnection.ClientStream, "Server->Client", clientEndpoint);
                
                // Wait for either direction to complete
                await Task.WhenAny(clientToServerTask, serverToClientTask);
                
                if (_options.Verbose)
                {
                    Console.WriteLine($"Relay connection {clientEndpoint} terminated");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in relay connection {clientEndpoint}: {ex.Message}");
            }
            finally
            {
                // Clean up
                _connections.TryRemove(clientEndpoint, out _);
                
                try
                {
                    clientSocket?.Close();
                    serverSocket?.Close();
                }
                catch { }
            }
        }

        private async Task ForwardDataAsync(Stream fromStream, Stream toStream, string direction, string connectionId)
        {
            var buffer = new byte[32768]; // 32KB buffer for optimal performance
            
            try
            {
                while (true)
                {
                    var bytesRead = await fromStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        if (_options.Verbose)
                        {
                            Console.WriteLine($"Connection {connectionId}: {direction} stream closed");
                        }
                        break;
                    }
                    
                    // Log TDS packets if verbose and debug enabled
                    if (_options.Verbose && _options.Debug && bytesRead >= 8)
                    {
                        try
                        {
                            var header = TdsProtocol.ParseTdsHeader(buffer);
                            Console.WriteLine($"{direction} [{connectionId}]: TDS Type=0x{header.Type:X2}, Length={header.Length}");
                        }
                        catch
                        {
                            Console.WriteLine($"{direction} [{connectionId}]: Non-TDS data, {bytesRead} bytes");
                        }
                    }
                    else if (_options.Verbose)
                    {
                        Console.WriteLine($"{direction} [{connectionId}]: {bytesRead} bytes");
                    }
                    
                    // Forward the data as-is
                    await toStream.WriteAsync(buffer, 0, bytesRead);
                    await toStream.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                if (_options.Debug)
                {
                    Console.WriteLine($"Error forwarding data {direction} for {connectionId}: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            
            // Close all active connections
            foreach (var connection in _connections.Values)
            {
                connection.Close();
            }
            _connections.Clear();
            
            Console.WriteLine("MSSQL Relay stopped");
        }
    }

    public class RelayConnection
    {
        public TcpClient ClientSocket { get; }
        public TcpClient ServerSocket { get; }
        public Stream ClientStream { get; }
        public Stream ServerStream { get; }
        public string ClientEndpoint { get; }
        public DateTime CreatedAt { get; }

        public RelayConnection(TcpClient clientSocket, TcpClient serverSocket, string clientEndpoint)
        {
            ClientSocket = clientSocket;
            ServerSocket = serverSocket;
            ClientStream = clientSocket.GetStream();
            ServerStream = serverSocket.GetStream();
            ClientEndpoint = clientEndpoint;
            CreatedAt = DateTime.Now;
        }

        public void Close()
        {
            try
            {
                ClientSocket?.Close();
                ServerSocket?.Close();
            }
            catch { }
        }
    }

    public class RelayOptions
    {
        public int Port { get; set; } = 1433;
        public string BindAddress { get; set; } = "0.0.0.0";
        public string ServerHost { get; set; } = "localhost";
        public int ServerPort { get; set; } = 1433;
        public bool Verbose { get; set; } = false;
        public bool Debug { get; set; } = false;
    }
}
