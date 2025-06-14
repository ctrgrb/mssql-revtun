# RevTun Usage Examples

## How to Use the MSSQL Reverse Tunnel

This document provides step-by-step instructions for using the RevTun reverse tunnel system.

### Architecture Overview

```
[External Client] → [Port 1080 on Server] → [MSSQL Protocol] → [RevTun Client] → [Target Server]
```

1. **Server**: Listens on port 1433 (MSSQL) and opens port 1080 (proxy) when a client connects
2. **Client**: Connects to server via MSSQL protocol, receives tunnel requests, and proxies to target hosts
3. **Proxy**: External applications connect to port 1080 on the server to access resources through the client

### Step-by-Step Usage

#### 1. Start the Server
```bash
# In terminal 1
dotnet run server
# or with options:
dotnet run server --verbose --port 1433 --proxy-port 1080
```
Expected output:
```
MSSQL Server started on 0.0.0.0:1433
Proxy service ready on port 1080 (will activate when client connects)
Waiting for client connections...
```

#### 2. Start the Client
```bash
# In terminal 2
dotnet run client
# or with options:
dotnet run client --host localhost --verbose
```
Expected output:
```
Connecting to MSSQL Server at localhost:1433...
Connected to server!
Login successful! Tunnel is now active.

=== MSSQL Reverse Tunnel Client ===
This client is now connected and ready to handle tunnel requests.
The server should have activated a proxy on port 1080.
```

Server should now show:
```
MSSQL Client connected: 127.0.0.1:xxxxx
✓ Proxy tunnel activated on port 1080
```

#### 3. Test the Tunnel

##### Using curl with HTTP proxy
```bash
# Test basic HTTP request
curl --proxy localhost:1080 http://httpbin.org/ip

# Test HTTPS request
curl --proxy localhost:1080 https://httpbin.org/ip
```

##### Using curl with CONNECT method
```bash
# Test direct CONNECT
curl -v --proxy localhost:1080 https://google.com
```

##### Using proxychains
```bash
# Install proxychains (if not already installed)
# Ubuntu/Debian: sudo apt install proxychains
# CentOS/RHEL: sudo yum install proxychains-ng

# Configure proxychains
echo "socks5 127.0.0.1 1080" >> ~/.proxychains/proxychains.conf

# Use proxychains
proxychains curl http://httpbin.org/ip
proxychains nmap -sT 8.8.8.8 -p 80,443
```

##### Using browser
1. Configure your browser to use HTTP proxy: `localhost:1080`
2. Browse to any website
3. Traffic will be tunneled through the MSSQL client

### Traffic Flow Example

1. **External Request**: `curl --proxy localhost:1080 http://example.com`
2. **Server Receives**: HTTP CONNECT request on port 1080
3. **Server Creates**: TDS TUNNEL_CONNECT packet: `connectionId=1, host=example.com, port=80`
4. **Client Receives**: TUNNEL_CONNECT via MSSQL protocol
5. **Client Connects**: TCP connection to example.com:80
6. **Client Responds**: TDS TUNNEL_CONNECT_ACK packet with success status
7. **Data Flow**: All subsequent data flows through TDS TUNNEL_DATA packets

### Network Traffic Analysis

Monitor the traffic to see the MSSQL protocol in action:

```bash
# Capture MSSQL traffic (port 1433)
sudo tcpdump -i lo -A port 1433

# Capture proxy traffic (port 1080)  
sudo tcpdump -i lo -A port 1080
```

You'll see:
- **Port 1433**: TDS protocol packets with tunnel data embedded
- **Port 1080**: Regular HTTP/SOCKS proxy traffic

### Testing Different Protocols

#### HTTP Requests
```bash
curl --proxy localhost:1080 http://httpbin.org/get
curl --proxy localhost:1080 -X POST http://httpbin.org/post -d "test=data"
```

#### HTTPS/TLS Requests
```bash
curl --proxy localhost:1080 https://httpbin.org/get
curl --proxy localhost:1080 https://www.google.com
```

#### SSH Through Tunnel
```bash
# Using ProxyCommand with netcat
ssh -o ProxyCommand='nc -X connect -x localhost:1080 %h %p' user@remotehost

# Or configure in ~/.ssh/config:
# Host remote-via-tunnel
#     HostName remotehost.example.com
#     ProxyCommand nc -X connect -x localhost:1080 %h %p
```

#### Database Connections
```bash
# MySQL through tunnel
mysql -h target-db-server -P 3306 --protocol=TCP

# PostgreSQL through tunnel  
psql -h target-db-server -p 5432 -U username database
```

### Monitoring and Debugging

#### Check Tunnel Status
In the client terminal, type:
```
status
```

This shows:
- Connection status
- Active tunnel connections
- Usage instructions

#### View Detailed Logs
Both server and client show detailed packet logs including:
- TDS packet headers (type, length, etc.)
- Hex dumps of packet contents  
- Connection events
- Error messages

#### Example Log Output
```
RECEIVED TDS Packet:
  Type: 0xF1 (TUNNEL_CONNECT)
  Status: 0x01
  Length: 32
  SPID: 0
  PacketID: 1
  Hex: F1 01 00 20 00 00 01 00 01 00 00 00 0B 00 67 6F 6F 67 6C 65 2E 63 6F 6D 50 00 00 00

Tunnel connect request: 1 -> google.com:80
Successfully connected to google.com:80 for tunnel 1
```

### Security Considerations

⚠️ **Important Security Notes**:

1. **Not for Production**: This is a demonstration tool, not production-ready
2. **No Encryption**: Data is not encrypted beyond basic TDS obfuscation
3. **Authentication**: Uses simulated SQL Server authentication
4. **Logging**: All traffic is logged - don't use with sensitive data
5. **Firewall**: May bypass network security controls

### Troubleshooting

#### "Connection Refused" on Port 1080
- Make sure the server is running first
- Ensure a client has connected (proxy only activates after client connects)
- Check that port 1080 is not blocked by firewall

#### "No Authenticated MSSQL Client Available"
- Client authentication failed
- Check server logs for TDS handshake errors
- Restart both server and client

#### Tunnel Connections Not Working
- Check that target hosts/ports are reachable from client machine
- Verify DNS resolution on client side
- Check client logs for connection errors

#### Performance Issues
- Large data transfers may be slow due to TDS overhead
- Consider adjusting buffer sizes for better performance
- Monitor memory usage with many concurrent connections

## Linux-Specific Usage

### Running on Linux

#### Using Framework-Dependent Build
```bash
# After building on Linux
cd revtun
dotnet bin/Release/net8.0/revtun.dll server --verbose
```

#### Using Self-Contained Binary
```bash
# After extracting Linux binary
chmod +x revtun
./revtun server --verbose
```

#### Background Execution
```bash
# Run server in background
nohup ./revtun server --verbose > revtun.log 2>&1 &

# Check process
ps aux | grep revtun

# Stop server
pkill -f revtun
```

### Linux Network Testing

#### Test with Common Linux Tools
```bash
# Test with wget
wget --proxy=http://localhost:1080 -O - http://httpbin.org/ip

# Test with curl
curl --proxy socks5://localhost:1080 http://httpbin.org/headers

# Test SSH tunneling
ssh -o ProxyCommand="nc -X 5 -x localhost:1080 %h %p" user@target-server

# Test with netcat
echo -e "GET / HTTP/1.1\r\nHost: example.com\r\n\r\n" | nc -x localhost:1080 example.com 80
```

#### Iptables Integration
```bash
# Redirect specific traffic through tunnel
sudo iptables -t nat -A OUTPUT -p tcp --dport 80 -j REDIRECT --to-port 1080

# Route specific destination through tunnel
sudo iptables -t nat -A OUTPUT -d 192.168.1.0/24 -p tcp -j REDIRECT --to-port 1080
```

### Linux Deployment Scenarios

#### Scenario 1: Server on DMZ, Client on Internal Network
```bash
# DMZ Server (accessible from internet)
./revtun server --bind 0.0.0.0 --port 1433 --proxy-port 1080 --verbose

# Internal Client (connects outbound)
./revtun client --host dmz-server.company.com --port 1433 --verbose
```

#### Scenario 2: Reverse SSH Alternative
```bash
# Target server (where you want to access resources)
./revtun client --host external-server.com --port 1433 --verbose

# External server (accessible from internet)
./revtun server --bind 0.0.0.0 --port 1433 --proxy-port 1080 --verbose

# Access internal resources from anywhere
curl --proxy external-server.com:1080 http://internal-app.company.com
```

#### Scenario 3: Container Deployment
```bash
# Run server in Docker
docker run -d --name revtun-server -p 1433:1433 -p 1080:1080 revtun:latest server --verbose

# Run client in Docker
docker run --rm --name revtun-client --link revtun-server revtun:latest client --host revtun-server --verbose

# Test from host
curl --proxy localhost:1080 http://httpbin.org/ip
```

### Linux Troubleshooting

#### Permission Issues
```bash
# If port binding fails
sudo setcap 'cap_net_bind_service=+ep' ./revtun

# Or run with elevated permissions
sudo ./revtun server --port 1433 --proxy-port 1080
```

#### Network Connectivity
```bash
# Check if ports are listening
netstat -tulpn | grep -E ":(1433|1080)"
ss -tulpn | grep -E ":(1433|1080)"

# Test connectivity
telnet localhost 1433
nc -zv localhost 1433

# Check firewall
sudo ufw status
sudo iptables -L
```

#### Process Management
```bash
# Find RevTun processes
ps aux | grep revtun
pgrep -f revtun

# Monitor network connections
lsof -i :1433
lsof -i :1080

# Check system logs
journalctl -u revtun
tail -f /var/log/syslog | grep revtun
```

#### Performance Monitoring
```bash
# Monitor network usage
iftop -i eth0
nethogs
ss -i

# Monitor system resources
htop
iostat 1
```
