# RevTun - MSSQL Reverse Tunnel

A reverse tunneling tool that disguises network traffic as MSSQL communications using the TDS protocol.

## How It Works

Server listens on port 1433 (MSSQL), opens proxy on port 1080 when client connects. Client connects using TDS protocol and proxies traffic to real destinations.

## Prerequisites

- .NET 8.0 or later

## Building

```bash
# compile for windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true

# compile for linux
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true
```

## Usage

### Start Server
```bash
dotnet run server
dotnet run server --port 1435 --proxy-port 8080 --verbose
```

### Start Client
```bash
dotnet run client
dotnet run client --host server.example.com --port 1435 --verbose
```

### Use Proxy
```bash
# HTTP requests
curl --proxy localhost:1080 http://httpbin.org/ip

# HTTPS requests (TLS/SSL)
curl --proxy localhost:1080 https://httpbin.org/ip
```

## Options

### Server
- `--port, -p <port>` - MSSQL server port (default: 1433)
- `--proxy-port <port>` - Proxy port (default: 1080)
- `--bind <address>` - Bind address (default: 0.0.0.0)
- `--verbose, -v` - Enable logging
- `--require-encryption` - Require TLS encryption for all connections
- `--no-encryption` - Disable TLS encryption support

### Client
- `--host, -h <hostname>` - Server hostname (default: localhost)
- `--port, -p <port>` - Server port (default: 1433)
- `--username, -u <user>` - SQL username (default: sa)
- `--verbose, -v` - Enable logging
- `--encrypt` - Request TLS encryption
- `--require-encryption` - Require TLS encryption (fail if not supported)

## Encryption Support

RevTun now supports encrypted MSSQL tunnels using TLS/SSL encryption:

### Server Encryption Options
- **Default**: Supports encryption if client requests it
- `--require-encryption`: Forces all connections to use TLS encryption
- `--no-encryption`: Disables encryption support entirely

### Client Encryption Options
- **Default**: No encryption requested (plaintext tunnel)
- `--encrypt`: Requests TLS encryption from server
- `--require-encryption`: Requires TLS encryption (connection fails if server doesn't support it)

### Examples with Encryption
```bash
# Server requiring encryption
dotnet run server --require-encryption --verbose

# Client requesting encryption
dotnet run client --encrypt --host server.example.com

# Client requiring encryption
dotnet run client --require-encryption --host server.example.com
```

The encryption uses industry-standard TLS and protects both the tunnel traffic and the encapsulated data.

## Security Notes

RevTun now supports TLS encryption for enhanced security:

- **Plaintext Mode**: Basic TDS protocol obfuscation (legacy compatibility)
- **Encrypted Mode**: Full TLS encryption of tunnel traffic and data
- **Mixed Mode**: Server can accept both encrypted and plaintext connections (default)

When encryption is enabled, all traffic including tunnel data is protected by TLS.
