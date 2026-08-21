<div align="center">
  <img src="github.png" width="360" alt="ZapretDPI-TR" />
  <h1>ZapretDPI-TR</h1>
  <p><strong>Windows üzerindeki ağ yönlendirme, DPI yöntemleri, onarım işlemleri ve servis temizliğini tek yerden yöneten masaüstü yönetim aracı.</strong></p>

  <p>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/actions"><img src="https://img.shields.io/badge/CI-passing-2ea44f?style=flat-square&logo=githubactions&logoColor=white" alt="CI" /></a>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/releases"><img src="https://img.shields.io/badge/Release-passing-2ea44f?style=flat-square&logo=github&logoColor=white" alt="Release" /></a>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/releases/latest"><img src="https://img.shields.io/badge/s%C3%BCr%C3%BCm-v1.0.0-0078d4?style=flat-square" alt="Sürüm" /></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/lisans-MIT-blue?style=flat-square" alt="Lisans" /></a>
    <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue?style=flat-square&logo=windows&logoColor=white" alt="Platform" />
    <img src="https://img.shields.io/badge/.NET-10.0%20WPF-purple?style=flat-square&logo=dotnet&logoColor=white" alt=".NET" />
  </p>

  <p>
    <strong><a href="README.md">Türkçe</a></strong> • <strong><a href="README.en.md">English</a></strong> • <strong><a href="README.ru.md">Русский</a></strong>
  </p>
</div>

---

Türkiye ISS'leri (Türk Telekom, Superonline, Vodafone, Turkcell vb.) için optimize edilmiş, modern, bağımsız ve tam donanımlı **Windows DPI Aşım & Şifreli DNS Aracı** (C# 10 / WPF / Zapret2 LUA).

---

## 🌟 Öne Çıkan Özellikler

- ⚡ **Zapret2 (v1.0.4) LUA Çekirdeği:** Çift profil mimarisi ile Discord (ses/görüntü/gateway) ve Web trafiği (Roblox, web siteleri vb.) için bağımsız ve optimize edilmiş desync paket kuralları.
- 🛡️ **Gelişmiş Şifreli DNS (DNSCrypt-proxy):** ISS kaynaklı DNS zehirlenmesine karşı AdGuard DNSCrypt, Google DoH ve Quad9 şifreli DNS entegrasyonu (`127.0.0.1:53`).
- 🎮 **Ağ Paylaşımı (LAN Share):** PlayStation, Xbox, Akıllı TV gibi konsol ve ağdaki diğer cihazların Zapret üzerinden DPI aşımı yapabilmesi için `go-pcap2socks` desteği.
- ⚙️ **Windows Sistem Servisi (SCM):** Arka planda sessizce ve Windows açılışında otomatik çalışan sistem servisi modu.
- 🔍 **Canlı DNS & ISS Analizi (Blockcheck2):** Gerçek zamanlı alan adı çözümleme testi, gecikme (ping) ölçümü ve dahili Blockcheck2 ISS strateji tarayıcısı.
- 🔄 **Velopack Otomatik Güncelleme Motoru:** GitHub API entegrasyonu sayesinde yeni sürümleri tek tıkla otomatik denetleme ve arka planda güncelleme.
- 🌐 **Çoklu Dil Desteği:** Türkçe, İngilizce ve Rusça dil seçenekleri.
- 🎨 **Özgün Koyu Arayüz:** Modern, şık ve koyu mod diyalog pencereleriyle donatılmış yüksek performanslı WPF tasarımı.

---

## 💻 Sistem Gereksinimleri

- Windows 10 / 11 (64-bit)
- .NET 8.0 veya .NET 10.0 Desktop Runtime (Taşınabilir self-contained sürümde runtime dahildir)
- Yönetici Yetkisi (WinDivert sürücü erişimi için)

---

## 📦 Kurulum & Yayınlama (Velopack)

Projeyi derlemek ve Velopack kurulum paketi (`ZapretDPI-TR-win-Setup.exe`) oluşturmak için:

```powershell
# 1. Derleme
dotnet publish src/ZapretDPI.csproj -c Release -r win-x64 --self-contained true -o ./publish

# 2. Velopack Paketi Oluşturma
vpk pack -u ZapretDPI-TR -v 1.0.0 -p ./publish -e ZapretDPI-TR.exe -i src/icon.ico -s src/splash.png --splashProgressColor "#3B82F6" -o Releases
```

---

## 📜 Lisans ve Açık Kaynak Teşekkürleri (Credits & Attributions)

Bu proje açık kaynaklı yazılımlardan ve topluluk projelerinden güç almaktadır:

- **[Zapret & WinWS2](https://github.com/bol-van/zapret)** — Geliştirici: [bol-van](https://github.com/bol-van) (MIT License)  
  *Windows ve Linux için bağımsız DPI engeli aşma çekirdeği.*
- **[WinDivert](https://github.com/basil00/Divert)** — Geliştirici: [basil00](https://github.com/basil00) & req-q (LGPLv3 / GPLv2)  
  *Windows kullanıcı alanı paket yakalama ve yönlendirme çekirdek sürücüsü.*
- **[dnscrypt-proxy](https://github.com/DNSCrypt/dnscrypt-proxy)** — Geliştirici: [Frank Denis](https://github.com/jedisct1) ve DNSCrypt Ekibi (ISC License)  
  *DNSCrypt v2 ve DoH (DNS over HTTPS) protokollerini destekleyen güvenli şifreli DNS istemcisi.*
- **[go-pcap2socks](https://github.com/zhxie/go-pcap2socks)** — Geliştirici: [zhxie](https://github.com/zhxie) (MIT License)  
  *Yerel ağdaki cihazların trafiğini SOCKS/DPI tüneline aktaran yönlendirici araç.*
- **[Velopack](https://github.com/velopack/velopack)** — Velopack Ekibi (MIT License)  
  *Windows için modern kurulum ve otomatik güncelleme motoru.*

ZapretDPI-TR projesinin kendisi **[MIT Lisansı](LICENSE)** altında lisanslanmıştır.

---

## ⚠️ Sorumluluk Reddi (Disclaimer)

Bu araç, ağ analizi, dijital gizlilik ve sansüre karşı araştırma amacıyla geliştirilmiştir. Kullanıcılar, aracın kullanımından ve yerel mevzuatlara uygunluğundan kendileri sorumludur.
