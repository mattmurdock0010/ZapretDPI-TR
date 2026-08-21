<div align="center">
  <img src="github.png" width="360" alt="ZapretDPI-TR" />
  <h1>ZapretDPI-TR</h1>
  <p><strong>A desktop management tool that manages network routing, DPI bypass methods, repair procedures, and service cleanup on Windows in one place.</strong></p>

  <p>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/actions"><img src="https://img.shields.io/badge/CI-passing-2ea44f?style=flat-square&logo=githubactions&logoColor=white" alt="CI" /></a>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/releases"><img src="https://img.shields.io/badge/Release-passing-2ea44f?style=flat-square&logo=github&logoColor=white" alt="Release" /></a>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/releases/latest"><img src="https://img.shields.io/badge/version-v1.0.0-0078d4?style=flat-square" alt="Version" /></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" alt="License" /></a>
    <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue?style=flat-square&logo=windows&logoColor=white" alt="Platform" />
    <img src="https://img.shields.io/badge/.NET-10.0%20WPF-purple?style=flat-square&logo=dotnet&logoColor=white" alt=".NET" />
  </p>

  <p>
    <strong><a href="README.md">Türkçe</a></strong> • <strong><a href="README.en.md">English</a></strong> • <strong><a href="README.ru.md">Русский</a></strong>
  </p>
</div>

---

Modern, standalone and fully-featured **Windows DPI Bypass & Encrypted DNS Suite** (C# 10 / WPF / Zapret2 LUA) optimized for Turkish and global ISPs.

---

## 🌟 Key Features

- ⚡ **Zapret2 (v1.0.4) LUA Core:** Dual-profile architecture with independent, optimized desync packet filtering for Discord (voice/video/gateway) and Web traffic (Roblox, websites, etc.).
- 🛡️ **Advanced Encrypted DNS (DNSCrypt-proxy):** Integrated local encrypted DNS resolver (`127.0.0.1:53`) supporting AdGuard DNSCrypt, Google DoH, and Quad9 against ISP DNS poisoning.
- 🎮 **LAN Sharing (go-pcap2socks):** Route PlayStation, Xbox, Smart TV and local network device traffic through Zapret DPI bypass.
- ⚙️ **Windows System Service (SCM):** Run silently in the background and start automatically on Windows startup.
- 🔍 **Live DNS & ISP Diagnostics (Blockcheck2):** Real-time domain resolution tester, round-trip latency measurements, and integrated Blockcheck2 ISP strategy scanner.
- 🔄 **Velopack Auto-Updater:** Seamless one-click background update engine integrated with GitHub Releases.
- 🌐 **Multi-Language UI:** Native support for English, Turkish, and Russian.
- 🎨 **Modern Dark Aesthetics:** Responsive and high-performance WPF interface with custom dark modal dialogs.

---

## 💻 System Requirements

- Windows 10 / 11 (64-bit)
- .NET 8.0 or .NET 10.0 Desktop Runtime (Runtime included in portable self-contained build)
- Administrator Privileges (for WinDivert kernel packet diversion)

---

## 📦 Build & Package (Velopack)

To compile the application and generate the Velopack setup installer (`ZapretDPI-TR-win-Setup.exe`):

```powershell
# 1. Compile
dotnet publish src/ZapretDPI.csproj -c Release -r win-x64 --self-contained true -o ./publish

# 2. Package with Velopack
vpk pack -u ZapretDPI-TR -v 1.0.0 -p ./publish -e ZapretDPI-TR.exe -i src/icon.ico -s src/splash.png --splashProgressColor "#3B82F6" -o Releases
```

---

## 📜 Credits & Open-Source Attributions

This project is powered by open-source technologies:

- **[Zapret & WinWS2](https://github.com/bol-van/zapret)** — Developed by [bol-van](https://github.com/bol-van) (MIT License)  
  *Deep packet inspection bypass core and LUA modules.*
- **[WinDivert](https://github.com/basil00/Divert)** — Developed by [basil00](https://github.com/basil00) & req-q (LGPLv3 / GPLv2)  
  *Windows user-mode packet capture and diversion kernel driver.*
- **[dnscrypt-proxy](https://github.com/DNSCrypt/dnscrypt-proxy)** — Developed by [Frank Denis](https://github.com/jedisct1) and DNSCrypt Team (ISC License)  
  *Flexible DNS proxy with support for DNSCrypt v2 and DNS-over-HTTPS.*
- **[go-pcap2socks](https://github.com/zhxie/go-pcap2socks)** — Developed by [zhxie](https://github.com/zhxie) (MIT License)  
  *Redirect traffic from local network devices to SOCKS/DPI bypass proxy.*
- **[Velopack](https://github.com/velopack/velopack)** — Velopack Team (MIT License)  
  *Modern installer and auto-update framework for desktop apps.*

The ZapretDPI-TR project itself is licensed under the **[MIT License](LICENSE)**.

---

## ⚠️ Disclaimer

This tool is designed for educational purposes, network diagnostics, and digital privacy research. Users are solely responsible for compliance with local regulations.
