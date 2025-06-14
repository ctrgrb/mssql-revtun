# RevTun - MSSQL Reverse Tunnel

A sophisticated reverse tunneling tool that disguises network traffic as Microsoft SQL Server (MSSQL) communications using the authentic Tabular Data Stream (TDS) protocol. When a client connects to the server, the server automatically opens a proxy port (1080) that tunnels all traffic through the client connection, making it appear as legitimate database traffic.

## 🎯 Key Features

### 🔄 Reverse Tunnel Functionality
- **Proxy Server**: Automatically activates SOCKS/HTTP proxy on port 1080 when client connects
- **Traffic Tunneling**: All proxy traffic is tunneled through the MSSQL connection
- **Bidirectional**: Client acts as the actual proxy, server forwards requests
- **Protocol Support**: HTTP, HTTPS, SOCKS5, and custom protocols

### 🛡️ Stealth Capabilities
- **TDS Protocol**: All traffic disguised as legitimate MSSQL database communications
- **Authentic Headers**: Uses real TDS packet structures and message types
- **Network Invisibility**: Traffic appears as normal database queries to network monitors
- **Port Legitimacy**: Uses standard MSSQL port (1433) for primary communication

### 🔌 Connectivity Options
- **HTTP Proxy**: `curl --proxy localhost:1080 http://example.com`
- **HTTPS Tunneling**: Support for SSL/TLS connections through CONNECT method
- **SOCKS5 Proxy**: Compatible with proxychains and other SOCKS clients
- **Multiple Protocols**: SSH, database connections, web browsing, etc.

## Quick Start

### Prerequisites
- .NET 8.0 or later
- Visual Studio 2022 or VS Code (optional)

### Building the Project
```bash
cd revtun
dotnet build
```

### Running the Reverse Tunnel

#### 1. Start the Server (Target Network)
```bash
# Basic server startup
dotnet run server

# Server with custom options
dotnet run server --port 1435 --proxy-port 8080 --bind 192.168.1.100 --verbose
```
Server will:
- Listen on specified port (default: 1433) for MSSQL connections
- Wait for client connections
- Activate proxy on specified port (default: 1080) when client connects

#### 2. Start the Client (Source Network)
```bash
# Basic client connection
dotnet run client

# Client with custom options
dotnet run client --host 192.168.1.100 --port 1435 --username admin --verbose

# Connection test mode
dotnet run client --host target.example.com --auto-exit
```
Client will:
- Connect to server via MSSQL protocol
- Perform TDS authentication handshake
- Wait for tunnel requests from server
- Proxy traffic to target destinations

#### 3. Use the Tunnel
```bash
# HTTP requests
curl --proxy localhost:1080 http://httpbin.org/ip

# HTTPS requests  
curl --proxy localhost:1080 https://www.google.com

# Using proxychains
proxychains curl http://example.com

# Browser configuration
# Set HTTP proxy: localhost:1080
```

### Command Line Options

#### Server Options
```bash
dotnet run server [options]

--port, -p <port>          MSSQL server port (default: 1433)
--proxy-port <port>        Proxy listener port (default: 1080)  
--bind <address>           Bind address (default: 0.0.0.0)
--verbose, -v              Enable verbose logging
```

#### Client Options
```bash
dotnet run client [options]

--host, -h <hostname>      Server hostname (default: localhost)
--port, -p <port>          Server port (default: 1433)
--username, -u <user>      SQL username (default: sa)
--password <pass>          SQL password (default: Password123)
--database, -d <db>        Database name (default: master)
--auto-exit                Exit after connection test
--verbose, -v              Enable verbose logging
```

### Testing the Setup
```bash
# Run automated tests (Linux/macOS)
chmod +x test.sh
./test.sh

# Run automated tests (Windows)
test.bat

# Manual testing with custom ports
dotnet run server --port 1435 --proxy-port 8080 &
dotnet run client --host localhost --port 1435
curl --proxy localhost:8080 http://httpbin.org/ip
```

## How It Works

### Architecture Overview
```
[Internet] ←→ [Proxy Port 1080] ←→ [MSSQL Port 1433] ←→ [TDS Protocol] ←→ [Client] ←→ [Target]
```

1. **Client Connection**: Client connects to server using MSSQL TDS protocol
2. **Proxy Activation**: Server opens proxy listener on port 1080
3. **Traffic Interception**: External applications connect to port 1080
4. **Protocol Wrapping**: Proxy traffic is wrapped in TDS packets
5. **Tunnel Transport**: TDS packets carry the tunneled data
6. **Client Forwarding**: Client unwraps and forwards to actual destinations
7. **Response Handling**: Responses follow the reverse path

### TDS Protocol Tunneling

The system uses custom TDS message types for tunnel communication:
- **TUNNEL_CONNECT (0xF1)**: Establish connection to target host
- **TUNNEL_DATA (0xF0)**: Transfer data through established tunnel
- **TUNNEL_DISCONNECT (0xF3)**: Close tunnel connection
- **TUNNEL_CONNECT_ACK (0xF2)**: Acknowledge connection status

All tunnel messages are embedded within legitimate TDS packet structures, making them indistinguishable from normal database traffic to network monitoring tools.

## Network Traffic Examples

## Network Traffic Examples

### Tunnel Establishment
```
CLIENT -> SERVER: Pre-Login (0x12)
  Contains: Version, Encryption, Instance options
  
SERVER -> CLIENT: Pre-Login Response (0x12)
  Contains: Server capabilities and preferences

CLIENT -> SERVER: TDS7 Login (0x10)
  Contains: Credentials, client info, connection options
  
SERVER -> CLIENT: Login Acknowledgment (0x04)
  Contains: Login success, server info
  [PROXY PORT 1080 ACTIVATED]
```

### Traffic Tunneling
```
EXTERNAL -> PROXY(1080): HTTP GET http://example.com
  
SERVER -> CLIENT: TUNNEL_CONNECT (0xF1)
  Contains: connectionId=1, host=example.com, port=80
  
CLIENT -> TARGET: TCP Connect to example.com:80

CLIENT -> SERVER: TUNNEL_CONNECT_ACK (0xF2)
  Contains: connectionId=1, success=true

SERVER -> CLIENT: TUNNEL_DATA (0xF0)
  Contains: connectionId=1, HTTP request data
  
CLIENT -> TARGET: Forward HTTP request

TARGET -> CLIENT: HTTP response

CLIENT -> SERVER: TUNNEL_DATA (0xF0)  
  Contains: connectionId=1, HTTP response data

SERVER -> PROXY(1080): Forward HTTP response
```

### Stealth Characteristics
- All traffic appears as MSSQL database queries
- Uses legitimate TDS packet structures
- Maintains proper SQL Server version negotiation
- Includes realistic authentication sequences
- Custom tunnel messages use reserved message type range

## Code Structure

### Core Components
- **`TdsProtocol.cs`**: TDS protocol implementation and packet creation
- **`MssqlServer.cs`**: Server-side TDS handler
- **`MssqlClient.cs`**: Client-side TDS implementation
- **`MssqlTrafficUtils.cs`**: Utilities for traffic analysis and logging

### Key Classes
- **`TdsHeader`**: Represents TDS packet header structure
- **`MssqlServer`**: TCP server that handles TDS connections
- **`MssqlClient`**: TCP client that speaks TDS protocol
- **`TdsProtocol`**: Static methods for packet creation and parsing

## Advanced Features

### Packet Analysis
The application provides detailed analysis of every TDS packet:
- Hex dumps with ASCII representation
- Header field breakdown
- Payload analysis based on message type
- Status flag interpretation

### Realistic Server Responses
The server generates authentic-looking responses:
- Proper column metadata for query results
- Realistic row data with multiple data types
- SQL Server version strings and capabilities
- Error handling and status codes

### Extensibility
The codebase is designed for easy extension:
- Add new TDS message types
- Implement additional SQL Server features
- Customize authentication mechanisms
- Add encryption support

## Use Cases

### 🔬 Network Analysis & Research
- **Protocol Analysis**: Study MSSQL TDS protocol behavior and structure
- **Traffic Patterns**: Analyze how database traffic flows through networks
- **Firewall Testing**: Test network security controls and detection systems
- **Penetration Testing**: Evaluate network defenses (authorized testing only)

### 🧪 Development & Testing
- **Proxy Development**: Test HTTP/SOCKS proxy implementations
- **Database Mocking**: Mock SQL Server for application testing
- **Network Simulation**: Simulate database connectivity scenarios
- **Load Testing**: Test application behavior with database-like traffic

### 📚 Educational Purposes
- **Protocol Learning**: Understand database communication protocols
- **Network Programming**: Learn about client-server networking
- **Security Education**: Study traffic analysis and tunneling techniques
- **Academic Research**: Support network security and protocol research

### 🔧 Specialized Applications
- **Network Traversal**: Bypass network restrictions using legitimate-looking traffic
- **Traffic Obfuscation**: Disguise network communications as database traffic
- **Connectivity Testing**: Test reachability through corporate proxies
- **Protocol Compatibility**: Verify TDS protocol implementations

## Configuration

### Server Settings
```csharp
private readonly int _port = 1433; // Default MSSQL port
```

### Client Settings
```csharp
private readonly string _server = "localhost";
private readonly int _port = 1433;
private readonly string _username = "sa";
private readonly string _password = "Password123";
private readonly string _database = "master";
```

## Troubleshooting

### Common Issues

**Port Already in Use**
- Make sure no other MSSQL instance is running
- Change the port number if needed
- Check firewall settings

**Connection Refused**
- Start the server before the client
- Verify the server is listening on the correct port
- Check network connectivity

**Authentication Failures**
- Verify username/password in client code
- Check server authentication logic
- Review TDS login packet format

## Security Considerations

⚠️ **Important**: This tool is for educational and testing purposes only.

- Passwords are transmitted in encoded but not encrypted form
- No SSL/TLS encryption is implemented
- Authentication is simulated, not real
- Do not use with production credentials

## Contributing

Contributions are welcome! Areas for improvement:
- Add more TDS message types
- Implement encryption support
- Add more realistic data generation
- Improve error handling
- Add unit tests

## License

This project is provided as-is for educational purposes. Use responsibly and in accordance with your organization's security policies.

## 📖 Documentation

- **[Linux Guide](LINUX.md)** - Comprehensive Linux compilation, deployment, and usage guide
- **[Usage Examples](USAGE.md)** - Detailed usage examples and scenarios
- **[Build Scripts](build.sh)** - Multi-platform build automation
- **[Docker Setup](Dockerfile)** - Container deployment configuration
- **[Test Scripts](test.sh)** - Automated testing and validation

## 📚 References

- [Microsoft TDS Protocol Documentation](https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-tds/)
- [SQL Server Network Protocols](https://docs.microsoft.com/en-us/sql/tools/configuration-manager/sql-server-network-configuration)
- [Tabular Data Stream Protocol](https://en.wikipedia.org/wiki/Tabular_Data_Stream)
- [.NET 8.0 Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [Docker Documentation](https://docs.docker.com/)

## 🐧 Linux Compilation & Deployment

### Prerequisites for Linux
```bash
# Install .NET 8.0 SDK on Ubuntu/Debian
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

# Install .NET 8.0 SDK on CentOS/RHEL/Fedora
sudo rpm --import https://packages.microsoft.com/keys/microsoft.asc
sudo wget -O /etc/yum.repos.d/microsoft-prod.repo https://packages.microsoft.com/config/centos/8/prod.repo
sudo dnf install dotnet-sdk-8.0

# Verify installation
dotnet --version
```

### Building for Linux

#### Option 1: Cross-Compilation from Windows/macOS
```bash
# Build Linux x64 binary
dotnet publish revtun -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Build Linux ARM64 binary (for Raspberry Pi, etc.)
dotnet publish revtun -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true
```

#### Option 2: Native Linux Build
```bash
# Clone the repository
git clone <your-repo-url>
cd revtun

# Build the project
dotnet build revtun

# Create release build
dotnet publish revtun -c Release --self-contained false

# Run on Linux
dotnet revtun/bin/Release/net8.0/revtun.dll server
```

#### Option 3: Multi-Platform Build Script
```bash
# Make build script executable
chmod +x build.sh

# Run automated build for all platforms
./build.sh

# Find Linux binaries in dist/ folder
ls -la dist/linux-x64/
```

### Linux Deployment

#### Single File Executable
```bash
# Extract pre-built Linux binary
tar -xzf revtun-linux-x64-<version>.tar.gz
cd linux-x64

# Make executable
chmod +x revtun

# Run server
./revtun server --port 1433 --verbose

# Run client (in another terminal)
./revtun client --host localhost --verbose
```

#### Docker Deployment
```bash
# Build Docker image
docker build -t revtun:latest .

# Run server in container
docker run -d --name revtun-server -p 1433:1433 -p 1080:1080 revtun:latest server --verbose

# Run client in container
docker run --rm --link revtun-server revtun:latest client --host revtun-server --verbose
```

#### Systemd Service (Linux)
Create `/etc/systemd/system/revtun.service`:
```ini
[Unit]
Description=RevTun MSSQL Reverse Tunnel Server
After=network.target

[Service]
Type=simple
User=revtun
WorkingDirectory=/opt/revtun
ExecStart=/opt/revtun/revtun server --bind 0.0.0.0 --verbose
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl daemon-reload
sudo systemctl enable revtun
sudo systemctl start revtun
sudo systemctl status revtun
```
