# WiFi Traffic

A native Windows network-visibility application for monitoring traffic on networks you own or are authorized to administer.

WiFi Traffic is a .NET 8 WPF desktop program that produces a real Windows EXE, stores history in SQLite, captures packets through Npcap/SharpPcap, identifies many website domains, and has an automatic Windows build workflow.

## Current features

- Native Windows desktop UI
- Real-time packet feed
- Adapter selector and start/stop capture
- Packet, byte, domain and source counters
- Local SQLite history
- Top website/domain view
- IPv4 and IPv6 capture
- TCP and UDP metadata
- DNS question extraction
- HTTP Host extraction
- TLS ClientHello SNI extraction
- Persistent data under LocalAppData
- Self-contained win-x64 publishing
- GitHub Actions Windows build
- One-click Windows setup script
- Desktop shortcut creation
- No packet payloads stored
- No HTTPS decryption
- No password/session interception

## Important: what "all Wi-Fi traffic" means on Windows

A normal Windows PC connected as an ordinary Wi-Fi client does not automatically receive every other device's private unicast packets. Promiscuous capture does not bypass how a modern access point or switch forwards traffic.

WiFi Traffic can record everything that reaches the selected capture adapter. To obtain whole-network visibility, the Windows machine needs to be in the traffic path, for example:

1. Use the monitoring PC as the network gateway/hotspot for the devices being monitored.
2. Feed mirrored traffic to the monitoring machine from networking hardware that supports port mirroring.
3. Use a dedicated gateway/router sensor and send metadata to this dashboard.

## Website visibility

Most modern websites use HTTPS. WiFi Traffic does not break or decrypt HTTPS.

It can still identify many domains from DNS lookups, TLS SNI when it is visible, and HTTP Host headers for unencrypted HTTP.

Encrypted DNS, TLS ECH, VPNs and some proxy technologies can reduce domain visibility. In those cases the app can still record visible connection metadata such as IP addresses, ports, protocol and byte counts.

## Install on Windows

### Requirements

- Windows 10 or Windows 11, 64-bit
- Administrator permission
- Npcap
- .NET 8 SDK only when building from source

### Easy source install

1. Download or clone this repository.
2. Install Npcap from the official Npcap website.
3. Double-click `windows-install.bat`.
4. Accept the Administrator prompt.
5. The script builds a self-contained `WifiTraffic.exe` and creates a desktop shortcut.
6. Select your active Wi-Fi or Ethernet adapter and click **Start capture**.

The generated application is placed in `dist\win-x64\WifiTraffic.exe`.

## GitHub build

Every push to `main` runs the **Windows Build** workflow. It restores packages, builds the solution, publishes a self-contained Windows x64 executable, and uploads `WifiTraffic-win-x64` as a workflow artifact.

## Data location

Traffic history is stored locally at `%LOCALAPPDATA%\WifiTraffic\wifi-traffic.db`.

Nothing in the application uploads captured traffic to a cloud service.

## Tech stack

- C# / .NET 8
- WPF
- SharpPcap 6.3.1
- PacketDotNet 1.4.8
- Microsoft.Data.Sqlite 8.0.30
- Npcap on Windows

## Privacy and authorization

Use WiFi Traffic only on networks and devices you own or have explicit authorization to monitor. The software is intentionally designed around traffic metadata and domain visibility rather than private message contents, credentials or decrypted HTTPS payloads.

## Roadmap

- Better device identification and naming
- Per-device charts
- Per-domain bandwidth charts
- Search and filters
- CSV/JSON export
- Windows tray mode
- Capture auto-start
- Gateway/hotspot assisted whole-network mode
- Alert rules
- Retention controls
- Packaged Windows installer
- Signed release builds
