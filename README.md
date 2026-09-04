# WiFi Traffic

A native Windows network-visibility application for monitoring traffic on networks you own or are authorized to administer.

WiFi Traffic is a .NET 8 WPF desktop program that produces a real Windows EXE, stores history in SQLite, captures local packet metadata through Npcap/SharpPcap, and can monitor DNS domains from devices across a normal home network through Router DNS Mode.

## Current features

- Native Windows desktop UI\n- No setup / Devices LAN discovery
- This-PC packet capture
- Whole Network / Router DNS Mode
- Devices stay connected to the normal router Wi-Fi
- Built-in UDP and TCP DNS proxy/sensor
- Per-device source IP for DNS queries
- Automatic Windows Firewall rules for local-subnet DNS
- Detects the PC LAN IP and default gateway
- Button to copy the DNS server address
- Button to open the router administration page
- Real-time traffic/domain feed
- Local SQLite history
- Top website/domain view
- DNS question extraction
- HTTP Host extraction in This-PC capture
- TLS ClientHello SNI extraction in This-PC capture
- GitHub Actions Windows build
- Self-contained Windows x64 EXE
- No HTTPS decryption
- No password/session interception


## No setup / Devices mode

If you do not want to log in to the router or change any network settings, select **No setup / Devices**.

This mode:

- requires no router login
- requires no DNS changes
- does not make phones, TVs or consoles reconnect to the PC
- scans the local LAN automatically
- shows discovered IP addresses
- shows MAC addresses where Windows can learn them
- attempts to resolve host/device names
- keeps a dedicated **Devices** tab in the dashboard

### What it cannot do

No-setup mode cannot show the websites used by other Wi-Fi devices.

That is a networking limitation, not a missing permission in the app: a normal Windows client does not receive the other clients' private unicast traffic. For domain visibility, use **Whole Network / Router DNS**. For full packet visibility, the router/gateway or network hardware must provide the traffic to the monitoring PC.

## Whole Network / Router DNS Mode

This is the recommended mode if you want to see domains requested by other devices **without connecting those devices to the computer**.

Phones, TVs, consoles, tablets and other PCs remain connected to the same normal router Wi-Fi they already use.

### How it works

WiFi Traffic runs a DNS server/proxy on the Windows PC.

Your router is configured once to hand out the Windows PC's LAN IP as the network DNS server. DNS requests then look like:

`Phone 192.168.1.24 → youtube.com`

`TV 192.168.1.31 → netflix.com`

`PlayStation 192.168.1.42 → playstation.net`

WiFi Traffic logs the device IP and requested domain, forwards the DNS request to an upstream resolver, and returns the answer to the device.

### Setup

1. Open WiFi Traffic as Administrator.
2. Select **Whole Network / Router DNS** at the top.
3. The program displays **This PC DNS address**.
4. Click **Copy DNS IP**.
5. Click **Open router**.
6. Log in to your router.
7. Find the router's **LAN**, **DHCP**, or **DNS** settings.
8. Set the LAN/DHCP **Primary DNS server** to the PC IP shown by WiFi Traffic.
9. Save the router setting.
10. Return to WiFi Traffic.
11. Click **Start DNS sensor**.

The other devices do **not** connect to the PC. They remain on the normal router Wi-Fi.

For the most reliable setup, reserve the PC's LAN IP in the router so the address does not change.

## Important limitations

A normal Windows Wi-Fi client cannot magically receive all private unicast packets from every other client on the router.

Router DNS Mode therefore provides **network-wide domain visibility**, not complete packet interception.

It can show many requested domains, but not full encrypted HTTPS URLs, page paths, messages, passwords, or page contents.

Visibility can also be reduced by:

- DNS-over-HTTPS
- DNS-over-TLS
- Apple Private Relay
- VPNs
- applications with hard-coded external DNS
- encrypted DNS inside browsers/apps
- cached DNS results

For full packet metadata from every device while they remain directly connected to the router, the router/network hardware itself must support features such as traffic mirroring, gateway capture, flow export, or compatible logging.

## This PC mode

This mode uses Npcap and captures packet metadata that reaches the selected Windows network adapter.

It can identify many domains from:

- ordinary DNS
- HTTP Host headers
- visible TLS SNI

Because modern switches and Wi-Fi access points isolate client unicast traffic, This PC mode normally sees mostly the Windows PC's own traffic.

# Installation guide

## Ready-made Windows build

1. Install Npcap from the official Npcap website if you want to use **This PC mode**.
2. Open this repository on GitHub.
3. Click **Actions**.
4. Open the newest successful **Windows Build**.
5. Download the **WifiTraffic-win-x64** artifact.
6. Extract the ZIP.
7. Run `WifiTraffic.exe`.
8. Accept the Administrator prompt.

Router DNS Mode itself does not require Npcap for DNS monitoring, but the application includes both modes.

## Build from source

Requirements:

- Windows 10 or Windows 11 64-bit
- Administrator permission
- .NET 8 SDK
- Npcap for packet-capture mode

Clone the repository:

```powershell
git clone https://github.com/chingchang2000/Wifi-Traffic.git
cd Wifi-Traffic
```

Then double-click:

`windows-install.bat`

or build manually:

```powershell
dotnet restore WifiTraffic.sln
dotnet build WifiTraffic.sln -c Release
dotnet publish src/WifiTraffic.App/WifiTraffic.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win-x64
```

The finished EXE is written to:

`dist\win-x64\WifiTraffic.exe`

## Router DNS troubleshooting

### DNS sensor says port 53 is already in use

Another DNS server may already be running on the PC, for example AdGuard Home, Pi-hole in a VM/container, Acrylic DNS Proxy or another DNS service.

Only one program can normally listen on the same IP/port 53.

### Other devices do not appear

Check that:

- Router DNS Mode is running.
- The router's LAN/DHCP DNS setting points to the Windows PC LAN IP.
- The PC is powered on.
- Windows Firewall is using a Private network profile.
- The client received new DHCP/DNS settings.
- The app/browser is not bypassing normal DNS using encrypted DNS or a VPN.

### Internet stops when WiFi Traffic is closed

If the router is configured to use the Windows PC as DNS, the PC DNS service needs to be running.

Either start WiFi Traffic again or restore the router's previous DNS setting.

### Why do I see a domain but not the exact page?

HTTPS encrypts the path and page contents. DNS normally reveals a hostname/domain, not the complete URL.

## Data location

Traffic history is stored locally at:

`%LOCALAPPDATA%\WifiTraffic\wifi-traffic.db`

Nothing in the application uploads captured traffic history to a cloud service.

## Tech stack

- C# / .NET 8
- WPF
- SharpPcap
- PacketDotNet
- Microsoft.Data.Sqlite
- Npcap for packet capture
- Built-in UDP/TCP DNS proxy for Router DNS Mode

## Privacy and authorization

Use WiFi Traffic only on networks and devices you own or are explicitly authorized to administer.

## Roadmap

- Better device naming/vendor identification
- Per-device domain views
- Router-specific setup helpers
- Search and filters
- CSV/JSON export
- Windows tray mode
- Auto-start DNS sensor
- Retention controls
- Router flow/log integrations
- Packaged Windows installer
- Signed release builds
