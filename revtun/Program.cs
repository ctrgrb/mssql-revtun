using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RevTun
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }            var command = args[0].ToLower();
              switch (command)
            {
                case "server":
                case "s":
                    await StartServer(args);
                    break;
                case "client":
                case "c":
                    await StartClient(args);
                    break;
                case "relay":
                case "r":
                    await StartRelay(args);
                    break;
                case "help":
                case "h":
                case "--help":
                case "-h":
                    PrintHelp();
                    break;
                default:
                    Console.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    break;
            }
        }
        
        static void PrintUsage()
        {
            Console.WriteLine("Usage: revtun [command] [options]");
            Console.WriteLine();            Console.WriteLine("Commands:");
            Console.WriteLine("  server, s      Start MSSQL server (listens on port 1433)");
            Console.WriteLine("  client, c      Start MSSQL client (connects to server)");
            Console.WriteLine("  relay, r       Start MSSQL relay (forwards between client and server)");
            Console.WriteLine("  help, h        Show detailed help");
            Console.WriteLine();            Console.WriteLine("Examples:");
            Console.WriteLine("  revtun server");
            Console.WriteLine("  revtun client");
            Console.WriteLine("  revtun relay --host server.example.com");
            Console.WriteLine("  revtun server --port 1433 --proxy-port 1080 --password testpass");
            Console.WriteLine("  revtun client --host localhost --port 1433 --password testpass");
            Console.WriteLine();
            Console.WriteLine("Use 'revtun help' for detailed options.");
        }
          static void PrintHelp()
        {
            Console.WriteLine("MSSQL Tunneling Tool");
            Console.WriteLine("==============================");
            Console.WriteLine();
            Console.WriteLine("SERVER OPTIONS:");
            Console.WriteLine("  --password <pass>          Password for authentication (REQUIRED)");
            Console.WriteLine("  --port, -p <port>          MSSQL server port (default: 1433)");
            Console.WriteLine("  --proxy-port <port>        Proxy listener port (default: 1080)");
            Console.WriteLine("  --bind <address>           Bind address (default: 0.0.0.0)");
            Console.WriteLine("  --verbose, -v              Enable verbose logging");
            Console.WriteLine("  --require-encryption       Require TLS encryption for all connections");
            Console.WriteLine("  --no-encryption            Disable TLS encryption support");
            Console.WriteLine();            Console.WriteLine("CLIENT OPTIONS:");
            Console.WriteLine("  --password <pass>          Password for authentication (REQUIRED)");
            Console.WriteLine("  --host, -h <hostname>      Server hostname (default: localhost)");
            Console.WriteLine("  --port, -p <port>          Server port (default: 1433)");
            Console.WriteLine("  --username, -u <user>      SQL username (default: sa)");
            Console.WriteLine("  --database, -d <db>        Database name (default: master)");            Console.WriteLine("  --auto-exit                Exit after connection test");
            Console.WriteLine("  --verbose, -v              Enable verbose logging");
            Console.WriteLine("  --debug                    Enable debug output (shows all messages)");
            Console.WriteLine("  --encrypt                  Request TLS encryption (default: enabled)");
            Console.WriteLine("  --no-encrypt               Disable TLS encryption");            Console.WriteLine("  --require-encryption       Require TLS encryption (fail if not supported)");
            Console.WriteLine();
            Console.WriteLine("RELAY OPTIONS:");
            Console.WriteLine("  --port, -p <port>          Relay listener port (default: 1433)");
            Console.WriteLine("  --bind <address>           Bind address (default: 0.0.0.0)");
            Console.WriteLine("  --host, -h <hostname>      Target server hostname (default: localhost)");
            Console.WriteLine("  --server-port <port>       Target server port (default: 1433)");
            Console.WriteLine("  --verbose, -v              Enable verbose logging");
            Console.WriteLine("  --debug                    Enable debug output (shows TDS packet details)");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("  # Start server on custom port");
            Console.WriteLine("  revtun server --port 1435 --proxy-port 8080 --password testpass");
            Console.WriteLine();
            Console.WriteLine("  # Connect client to remote server");
            Console.WriteLine("  revtun client --host 192.168.1.100 --port 1433 --password testpass");
            Console.WriteLine();
            Console.WriteLine("  # Start relay forwarding to remote server");
            Console.WriteLine("  revtun relay --host 192.168.1.100 --server-port 1433");
            Console.WriteLine();
        }        static async Task StartServer(string[] args)
        {
            var options = ParseServerOptions(args);
            
            // Validate that password is provided
            if (string.IsNullOrEmpty(options.Password))
            {
                Console.WriteLine("Error: Password is required for server authentication. Use --password <password>");
                Environment.Exit(1);
            }
            
            Console.WriteLine("\n=== Starting MSSQL Server ===");
            Console.WriteLine($"MSSQL Port: {options.Port}");
            Console.WriteLine($"Proxy Port: {options.ProxyPort}");
            Console.WriteLine($"Bind Address: {options.BindAddress}");
            Console.WriteLine($"Verbose: {options.Verbose}");
            Console.WriteLine($"Encryption Support: {(options.SupportEncryption ? "Enabled" : "Disabled")}");
            Console.WriteLine($"Require Encryption: {(options.RequireEncryption ? "Yes" : "No")}");
            Console.WriteLine("Note: This server simulates MSSQL TDS protocol behavior");
            Console.WriteLine("Press Ctrl+C to stop the server\n");
            
            var server = new MssqlServer(options);
            
            // Handle Ctrl+C gracefully
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nShutting down server...");
                server.Stop();
                Environment.Exit(0);
            };
            
            await server.StartAsync();
        }        static async Task StartClient(string[] args)
        {
            var options = ParseClientOptions(args);
            
            // Validate that password is provided
            if (string.IsNullOrEmpty(options.Password))
            {
                Console.WriteLine("Error: Password is required for client connection. Use --password <password>");
                Environment.Exit(1);
            }
            
            if (options.Debug)
            {
                Console.WriteLine("\n=== Starting MSSQL Client ===");
                Console.WriteLine($"Server: {options.Host}:{options.Port}");
                Console.WriteLine($"Username: {options.Username}");
                Console.WriteLine($"Database: {options.Database}");
                Console.WriteLine($"Auto Exit: {options.AutoExit}");
                Console.WriteLine($"Verbose: {options.Verbose}");
                Console.WriteLine($"Debug: {options.Debug}");
                Console.WriteLine($"Request Encryption: {(options.RequestEncryption ? "Yes" : "No")}");
                Console.WriteLine($"Require Encryption: {(options.RequireEncryption ? "Yes" : "No")}");
                Console.WriteLine("Note: This client will establish a reverse tunnel\n");
            }
            
            var client = new MssqlClient(options);
            await client.ConnectAsync();
            
            if (!options.AutoExit && options.Debug)
            {
                Console.WriteLine("\nClient session ended. Press any key to exit...");
                Console.ReadKey();
            }
        }
          static ServerOptions ParseServerOptions(string[] args)
        {
            var options = new ServerOptions();
            
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--port":
                    case "-p":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int port))
                        {
                            options.Port = port;
                            i++;
                        }
                        break;
                    case "--proxy-port":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int proxyPort))
                        {
                            options.ProxyPort = proxyPort;
                            i++;
                        }
                        break;
                    case "--bind":
                        if (i + 1 < args.Length)
                        {
                            options.BindAddress = args[i + 1];
                            i++;
                        }
                        break;
                    case "--verbose":
                    case "-v":
                        options.Verbose = true;
                        break;
                    case "--require-encryption":
                        options.RequireEncryption = true;
                        options.SupportEncryption = true;
                        break;                    case "--no-encryption":
                        options.SupportEncryption = false;
                        options.RequireEncryption = false;
                        break;
                    case "--password":
                        if (i + 1 < args.Length)
                        {
                            options.Password = args[i + 1];
                            i++;
                        }
                        break;
                }
            }
            
            return options;
        }
          static ClientOptions ParseClientOptions(string[] args)
        {
            var options = new ClientOptions();
            
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--host":
                    case "-h":
                        if (i + 1 < args.Length)
                        {
                            options.Host = args[i + 1];
                            i++;
                        }
                        break;
                    case "--port":
                    case "-p":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int port))
                        {
                            options.Port = port;
                            i++;
                        }
                        break;
                    case "--username":
                    case "-u":
                        if (i + 1 < args.Length)
                        {
                            options.Username = args[i + 1];
                            i++;
                        }
                        break;
                    case "--password":
                        if (i + 1 < args.Length)
                        {
                            options.Password = args[i + 1];
                            i++;
                        }
                        break;
                    case "--database":
                    case "-d":
                        if (i + 1 < args.Length)
                        {
                            options.Database = args[i + 1];
                            i++;
                        }
                        break;
                    case "--auto-exit":
                        options.AutoExit = true;
                        break;                    case "--verbose":
                    case "-v":
                        options.Verbose = true;
                        break;                    case "--debug":
                        options.Debug = true;
                        options.Verbose = true; // Debug mode implies verbose mode
                        break;                    case "--encrypt":
                        options.RequestEncryption = true;
                        break;
                    case "--no-encrypt":
                        options.RequestEncryption = false;
                        options.RequireEncryption = false;
                        break;
                    case "--require-encryption":
                        options.RequireEncryption = true;
                        options.RequestEncryption = true;
                        break;
                }
            }
              return options;
        }
        
        static async Task StartRelay(string[] args)
        {
            var options = ParseRelayOptions(args);
            
            Console.WriteLine("\n=== Starting MSSQL Relay ===");
            Console.WriteLine($"Relay Port: {options.Port}");
            Console.WriteLine($"Bind Address: {options.BindAddress}");
            Console.WriteLine($"Target Server: {options.ServerHost}:{options.ServerPort}");
            Console.WriteLine($"Verbose: {options.Verbose}");
            Console.WriteLine($"Debug: {options.Debug}");
            Console.WriteLine("Note: This relay transparently forwards MSSQL TDS protocol traffic");
            Console.WriteLine("Press Ctrl+C to stop the relay\n");
            
            var relay = new MssqlRelay(options);
            
            // Handle Ctrl+C gracefully
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nShutting down relay...");
                relay.Stop();
            };
            
            await relay.StartAsync();
        }
        
        static RelayOptions ParseRelayOptions(string[] args)
        {
            var options = new RelayOptions();
            
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--port":
                    case "-p":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int port))
                        {
                            options.Port = port;
                            i++;
                        }
                        break;
                    case "--bind":
                        if (i + 1 < args.Length)
                        {
                            options.BindAddress = args[i + 1];
                            i++;
                        }
                        break;
                    case "--host":
                    case "-h":
                        if (i + 1 < args.Length)
                        {
                            options.ServerHost = args[i + 1];
                            i++;
                        }
                        break;
                    case "--server-port":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int serverPort))
                        {
                            options.ServerPort = serverPort;
                            i++;
                        }
                        break;
                    case "--verbose":
                    case "-v":
                        options.Verbose = true;
                        break;
                    case "--debug":
                        options.Debug = true;
                        break;
                }
            }
            
            return options;
        }
    }
      public class ServerOptions
    {
        public int Port { get; set; } = 1433;
        public int ProxyPort { get; set; } = 1080;
        public string BindAddress { get; set; } = "0.0.0.0";
        public bool Verbose { get; set; } = false;
        public bool RequireEncryption { get; set; } = false;
        public bool SupportEncryption { get; set; } = true;
        public string Password { get; set; } = ""; // Server password for authentication
    }    public class ClientOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 1433;
        public string Username { get; set; } = "sa";
        public string Password { get; set; } = ""; // No default password - must be provided
        public string Database { get; set; } = "master";
        public bool AutoExit { get; set; } = false;
        public bool Verbose { get; set; } = false;
        public bool Debug { get; set; } = false;
        public bool RequestEncryption { get; set; } = true; // Enable encryption by default
        public bool RequireEncryption { get; set; } = false;
    }
}
