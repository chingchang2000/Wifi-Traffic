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

## Whole Network / Gateway Mode

If WiFi Traffic only shows websites opened by the monitoring PC, that is expected in **This PC only** mode.

A normal Windows PC connected to a router does not receive all private unicast traffic from every other Wi-Fi device. To see other devices without changing the router, use the new **Whole Network / Gateway** mode:

1. Open WiFi Traffic.
2. Change the mode at the top from **This PC only** to **Whole Network / Gateway**.
3. Click **Open Mobile Hotspot**.
4. Turn on Windows Mobile Hotspot.
5. Connect the phones, consoles, TVs, tablets or other PCs you want to monitor to the Windows hotspot instead of directly to the router Wi-Fi.
6. Return to WiFi Traffic.
7. Click **Refresh adapters**.
8. Select the adapter marked **★ WHOLE NETWORK**.
9. Click **Start capture**.

Traffic from hotspot clients will be labeled with directions such as:

- `Client → Internet`
- `Internet → Client`
- `Client / Local`

The **Source / device IP** column lets you distinguish hotspot clients by their private IP address.

### Important limitation

Whole Network mode can only see devices whose traffic actually passes through the monitoring PC.

Devices that remain connected directly to the original router Wi-Fi will normally remain invisible to the Windows capture machine. To monitor those without reconnecting them to the PC hotspot, the router/switch itself must support traffic mirroring, gateway logging, or another authorized network-monitoring method.

## Website visibility

Most modern websites use HTTPS. WiFi Traffic does not break or decrypt HTTPS.

It can still identify many domains from DNS lookups, TLS SNI when it is visible, and HTTP Host headers for unencrypted HTTP.

Encrypted DNS, TLS ECH, VPNs and some proxy technologies can reduce domain visibility. In those cases the app can still record visible connection metadata such as IP addresses, ports, protocol and byte counts.

# Installation guide

The easiest way to install WiFi Traffic is to use the pre-built Windows version from GitHub Actions.

## Method 1 — Download the ready-made Windows build

### Step 1: Install Npcap

WiFi Traffic needs Npcap to capture network traffic on Windows.

1. Go to the official Npcap website: https://npcap.com/#download
2. Download the latest Npcap installer.
3. Run the installer as Administrator.
4. Keep the normal/default installation options unless you know you need something different.
5. Finish the installation.

If WiFi Traffic was already open, close it and start it again after installing Npcap.

### Step 2: Download WiFi Traffic

1. Open this repository on GitHub.
2. Click the **Actions** tab near the top of the repository.
3. Click **Windows Build** in the left side.
4. Open the newest successful build with a green check mark.
5. Scroll down to **Artifacts**.
6. Download **WifiTraffic-win-x64**.
7. Extract the downloaded ZIP file somewhere on your PC.

For example:

`C:\Program Files\WifiTraffic\`

or:

`C:\Users\YOUR-NAME\Desktop\WifiTraffic\`

### Step 3: Start the program

1. Open the extracted folder.
2. Find `WifiTraffic.exe`.
3. Double-click it.
4. Windows will ask for Administrator permission because packet capture requires elevated access.
5. Click **Yes**.

If Windows SmartScreen appears because the build is not code-signed yet, choose **More info** and then **Run anyway** only if you downloaded the build directly from this repository.

### Step 4: Select your network adapter

At the top of WiFi Traffic there is an adapter selector.

Choose the adapter that is actually connected to the network:

- If your PC uses Wi-Fi, select your Wi-Fi adapter.
- If your PC uses a network cable, select the Ethernet adapter.
- Ignore disconnected adapters, VPN adapters and virtual adapters unless you specifically want to inspect them.

Then click:

**Start capture**

The status indicator should turn green and new network traffic should begin appearing in the **Live traffic** tab.

### Step 5: Test that it works

While capture is running:

1. Open your browser.
2. Visit a few websites.
3. Return to WiFi Traffic.
4. Look under **Live traffic** and **Top websites**.

You should begin seeing IP addresses, protocols, ports, traffic sizes and many detected domains.

Examples of domains that may appear:

- `youtube.com`
- `google.com`
- `discord.com`
- `tiktok.com`

Some encrypted traffic may only show IP/port information. This is normal.

---

## Method 2 — Build and install from source

Use this method if you want to modify the program or build it yourself.

### Requirements

You need:

- Windows 10 or Windows 11 64-bit
- Administrator permission
- Npcap
- .NET 8 SDK
- Git, if you want to clone the repository

### Step 1: Install Npcap

Download and install Npcap from:

https://npcap.com/#download

### Step 2: Install the .NET 8 SDK

Download the .NET 8 SDK from:

https://dotnet.microsoft.com/download/dotnet/8.0

Make sure you install the **SDK**, not only the runtime.

### Step 3: Download the repository

Either use Git:

```powershell
git clone https://github.com/chingchang2000/Wifi-Traffic.git
cd Wifi-Traffic
```

Or click:

**Code → Download ZIP**

and extract the ZIP file.

### Step 4: Run the automatic Windows installer

Double-click:

`windows-install.bat`

The installer will:

1. Request Administrator permission.
2. Check whether Npcap is installed.
3. Check whether the .NET 8 SDK is installed.
4. Restore the required NuGet packages.
5. Build WiFi Traffic.
6. Publish a self-contained Windows x64 version.
7. Create a desktop shortcut.
8. Start WiFi Traffic automatically.

The compiled application is placed here:

`dist\win-x64\WifiTraffic.exe`

After the first installation you can use:

`start.bat`

or the desktop shortcut to start WiFi Traffic again.

---

## Manual build

If you do not want to use the installer script, open PowerShell inside the repository and run:

```powershell
dotnet restore WifiTraffic.sln
dotnet build WifiTraffic.sln -c Release
dotnet publish src/WifiTraffic.App/WifiTraffic.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/win-x64
```

Then run:

`dist\win-x64\WifiTraffic.exe`

## Troubleshooting

### No adapters appear

Install or reinstall Npcap, then restart WiFi Traffic.

### Capture cannot start

Make sure:

- Npcap is installed.
- WiFi Traffic is running as Administrator.
- You selected a real Wi-Fi or Ethernet adapter.

### I do not see traffic from every device on my Wi-Fi

This is expected when the Windows PC is only a normal Wi-Fi client.

Modern Wi-Fi routers do not normally send every device's private traffic to every other connected device.

To monitor the entire network, the monitoring machine must be placed in the traffic path, for example by being used as a gateway/hotspot or by receiving mirrored traffic from compatible network equipment.

### Domains are sometimes missing

This can happen with:

- DNS-over-HTTPS
- DNS-over-TLS
- TLS ECH
- VPN connections
- proxies
- applications that connect directly to IP addresses

WiFi Traffic does not decrypt HTTPS traffic.

### Where is the traffic history stored?

The local database is stored at:

`%LOCALAPPDATA%\WifiTraffic\wifi-traffic.db`

You can delete the history from inside the application with **Clear history**.

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
- Whole Network / Gateway mode via Windows Mobile Hotspot
- Alert rules
- Retention controls
- Packaged Windows installer
- Signed release builds
