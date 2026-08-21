<div align="center">
  <img src="github.png" width="360" alt="ZapretDPI-TR" />
  <h1>ZapretDPI-TR</h1>
  <p><strong>Инструмент управления маршрутизацией, методами обхода DPI, процедурами восстановления и очисткой служб в Windows в одном месте.</strong></p>

  <p>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/actions"><img src="https://img.shields.io/badge/CI-passing-2ea44f?style=flat-square&logo=githubactions&logoColor=white" alt="CI" /></a>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/releases"><img src="https://img.shields.io/badge/Release-passing-2ea44f?style=flat-square&logo=github&logoColor=white" alt="Release" /></a>
    <a href="https://github.com/mattmurdock0010/ZapretDPI-TR/releases/latest"><img src="https://img.shields.io/badge/%D0%B2%D0%B5%D1%80%D1%81%D0%B8%D1%8F-v1.0.0-0078d4?style=flat-square" alt="Версия" /></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/%D0%BB%D0%B8%D1%86%D0%B5%D0%BD%D0%B7%D0%B8%D1%8F-MIT-blue?style=flat-square" alt="Лицензия" /></a>
    <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-blue?style=flat-square&logo=windows&logoColor=white" alt="Платформа" />
    <img src="https://img.shields.io/badge/.NET-10.0%20WPF-purple?style=flat-square&logo=dotnet&logoColor=white" alt=".NET" />
  </p>

  <p>
    <strong><a href="README.md">Türkçe</a></strong> • <strong><a href="README.en.md">English</a></strong> • <strong><a href="README.ru.md">Русский</a></strong>
  </p>
</div>

---

Современный автономный инструмент **обхода DPI и зашифрованного DNS для Windows** (C# 10 / WPF / Zapret2 LUA).

---

## 🌟 Основные возможности

- ⚡ **Ядро Zapret2 (v1.0.4) LUA:** Двухпрофильная архитектура с независимыми оптимизированными правилами desync для Discord (голос/видео/шлюз) и веб-трафика (Roblox, сайты и др.).
- 🛡️ **Зашифрованный DNS (DNSCrypt-proxy):** Встроенный локальный резолвер (`127.0.0.1:53`) с поддержкой AdGuard DNSCrypt, Google DoH и Quad9 для защиты от DNS-спуфинга.
- 🎮 **Раздача по локальной сети (LAN):** Маршрутизация трафика PlayStation, Xbox, Smart TV и других устройств через обход DPI с помощью `go-pcap2socks`.
- ⚙️ **Системная служба Windows (SCM):** Фоновая работа и автоматический запуск при старте Windows.
- 🔍 **Живая диагностика DNS и блокировок (Blockcheck2):** Проверка DNS-спуфинга в реальном времени, замер задержки и встроенный сканер Blockcheck2.
- 🔄 **Автообновление через Velopack:** Фоновая загрузка и установка обновлений в один клик через GitHub Releases.
- 🌐 **Многоязычный интерфейс:** Поддержка русского, английского и турецкого языков.
- 🎨 **Современный темный интерфейс:** Быстрый и плавный интерфейс WPF с фирменными модальными окнами.

---

## 💻 Системные требования

- Windows 10 / 11 (64-bit)
- .NET 8.0 или .NET 10.0 Desktop Runtime (В автономной portable версии среда выполнения уже включена)
- Права администратора (для драйвера WinDivert)

---

## 📦 Сборка и создание инсталлятора (Velopack)

Для компиляции и создания установочного пакета (`ZapretDPI-TR-win-Setup.exe`):

```powershell
# 1. Компиляция
dotnet publish src/ZapretDPI.csproj -c Release -r win-x64 --self-contained true -o ./publish

# 2. Упаковка через Velopack
vpk pack -u ZapretDPI-TR -v 1.0.0 -p ./publish -e ZapretDPI-TR.exe -i src/icon.ico -s src/splash.png --splashProgressColor "#3B82F6" -o Releases
```

---

## 📜 Лицензии и благодарности (Credits & Attributions)

Проект использует следующие открытые разработки:

- **[Zapret & WinWS2](https://github.com/bol-van/zapret)** — Разработчик: [bol-van](https://github.com/bol-van) (MIT License)  
  *Ядро обхода блокировок DPI и модули LUA.*
- **[WinDivert](https://github.com/basil00/Divert)** — Разработчики: [basil00](https://github.com/basil00) & req-q (LGPLv3 / GPLv2)  
  *Драйвер захвата и модификации сетевых пакетов в режиме пользователя Windows.*
- **[dnscrypt-proxy](https://github.com/DNSCrypt/dnscrypt-proxy)** — Разработчики: [Frank Denis](https://github.com/jedisct1) и команда DNSCrypt (ISC License)  
  *Клиент зашифрованного DNS с поддержкой DNSCrypt v2 и DoH.*
- **[go-pcap2socks](https://github.com/zhxie/go-pcap2socks)** — Разработчик: [zhxie](https://github.com/zhxie) (MIT License)  
  *Инструмент перенаправления трафика устройств локальной сети в прокси.*
- **[Velopack](https://github.com/velopack/velopack)** — Команда Velopack (MIT License)  
  *Современный фреймворк установки и автообновления.*

Проект ZapretDPI-TR распространяется под лицензией **[MIT License](LICENSE)**.

---

## ⚠️ Отказ от ответственности (Disclaimer)

Инструмент разработан исключительно для образовательных целей, сетевой диагностики и исследований в области цифровой приватности.
