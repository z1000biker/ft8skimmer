# RtlFt8Skimmer

**A multiband FT8 skimmer for the RTL-SDR Blog V4.** One inexpensive dongle, every HF band, a live propagation map.

[![build](https://github.com/z1000biker/ft8skimmer/actions/workflows/build.yml/badge.svg)](https://github.com/z1000biker/ft8skimmer/actions/workflows/build.yml)
![platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20Raspberry%20Pi-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

FT8 is synchronised to 15-second slots worldwide. RtlFt8Skimmer uses that rhythm: during each slot it listens to one band, decodes it with WSJT-X's `jt9`, and then hops to the next band for the following slot. The result is a station that watches 160 m to 6 m at the same time, plots every station it hears on a great-circle map and, if you like, reports the spots to [PSKReporter](https://pskreporter.info).

Two editions share the same engine:

- **Windows:** a desktop app (WPF).
- **Linux and Raspberry Pi:** a small server with the same interface in your browser. Run it on a Pi next to the antenna and watch the map from any PC or phone on your network.

Multiband skimmers usually need a KiwiSDR, a Red Pitaya or several receivers. This one needs an RTL-SDR Blog V4, whose built-in upconverter covers HF without direct sampling, and a piece of wire.

---

## Contents

- [Features](#features)
- [Quick start: Windows](#quick-start-windows)
- [Quick start: Linux and Raspberry Pi](#quick-start-linux-and-raspberry-pi)
- [Using the app](#using-the-app)
- [How it works](#how-it-works)
- [Building from source](#building-from-source)
- [Testing without hardware](#testing-without-hardware)
- [Troubleshooting](#troubleshooting)
- [Project layout](#project-layout)
- [Credits](#credits)

---

## Features

- **All HF bands with one dongle.** 160, 80, 60, 40, 30, 20, 17, 15, 12, 10 and 6 m. Pick any combination.
- **Slot-parity aware hopping.** FT8 stations transmit on even *or* odd slots. Naive round-robin over an even number of bands would always visit a band on the same parity and miss half of its stations. The scheduler rotates the order every round, so every band is heard on both.
- **Live map.** Great-circle paths from your locator to every decoded station, colour-coded per band, with a legend you can click to hide bands. Spots fade after 30 minutes.
- **Waterfall** of the current band (0–3 kHz), with a coloured marker at every band change.
- **Decode list** with UTC, band, SNR, DT, audio offset, message and distance. CQ calls are green; anyone calling *you* is highlighted in red.
- **Stations-per-band counter** for the last 30 minutes, so you can see at a glance which bands are open.
- **PSKReporter upload** over IPFIX/UDP, batched every 5 minutes and de-duplicated per band. A test-server switch lets you check everything before going live.
- **Diagnostics:** Invert IQ (spectrum swap) and Save WAV (keep every slot for inspection in WSJT-X).
- **Safe library loading.** The bundled V4-aware `rtlsdr` library is loaded from the application folder first, never from `PATH`, so another program's incompatible copy (SatDump, old SDR#, …) can't be picked up by accident.
- **Headless on Linux.** Runs as a systemd service, starts receiving at boot, and serves the live page to every browser on the network. Several browsers can watch at once, and a page reload replays the last 500 decodes.

## Quick start: Windows

1. **Driver.** Install the WinUSB driver for the dongle with [Zadig](https://zadig.akeo.ie/), exactly as for SDR# or SDR++. If one of those already works with your V4, you are done.
2. **WSJT-X.** Install [WSJT-X](https://wsjt.sourceforge.io/wsjtx.html). RtlFt8Skimmer uses its `jt9.exe` decoder and looks for it in `C:\WSJT\wsjtx\bin\`.
3. **Download** the latest `RtlFt8Skimmer-…-win-x64.zip` from [Releases](https://github.com/z1000biker/ft8skimmer/releases), unzip anywhere and run `RtlFt8Skimmer.exe`. The .NET runtime and the V4-aware `rtlsdr.dll` are included.
4. **Set up:** type your **callsign** and **locator** (4 or 6 characters, e.g. `JN58` or `JN58td`), tick the bands you want and press **Start**.
5. **Clock.** FT8 needs your PC clock within about ±1 s. Use a time-sync tool such as BktTimeSync or Meinberg NTP.

Connect an HF antenna to the V4. A random wire of 5–10 m works for a first test; a resonant antenna outdoors works much better.

## Quick start: Linux and Raspberry Pi

Packages for **x64** and **ARM64** (Raspberry Pi 4/5 with a 64-bit OS) are on the [Releases](https://github.com/z1000biker/ft8skimmer/releases) page. They are self-contained: no .NET installation is needed, and the V4-aware `librtlsdr` built from rtl-sdr-blog is included. Built on Ubuntu 22.04, so they run on Ubuntu 22.04 or newer, Debian 12, Raspberry Pi OS (Bookworm, 64-bit) and similar.

```
sudo apt install wsjtx libusb-1.0-0           # jt9 decoder and USB library
tar xzf rtlft8skimmer-v0.2.0-linux-arm64.tar.gz   # or …-linux-x64
cd rtlft8skimmer-v0.2.0-linux-arm64
sudo ./install.sh                             # installs to /opt/rtlft8skimmer + udev rule
rtlft8skimmer
```

Then open `http://<machine>:8073/` in a browser, enter your callsign and locator, pick bands and press **Start**.

To start receiving automatically at boot:

```
sudo ./install.sh --service
```

This enables a systemd service for your user that starts the skimmer with `--start` and your saved settings. Check it with `systemctl status rtlft8skimmer@$USER`.

Command-line options:

| Option | Meaning |
|---|---|
| `--port N` | HTTP port (default 8073) |
| `--bind ADDR` | Listen address. Default `0.0.0.0` (whole network); use `127.0.0.1` for this machine only |
| `--start` | Start receiving immediately with the saved settings |
| `--iq-file PATH` / `--iq-start UTC` | Play back a raw 8-bit IQ recording (960 kS/s) instead of the dongle |

Settings are stored in `~/.config/rtlft8skimmer/settings.json`. Keep the clock synced (`timedatectl` should show *System clock synchronized: yes*).

Anyone who can reach the port can start, stop and reconfigure the skimmer. On an untrusted network, use `--bind 127.0.0.1` and an SSH tunnel.

## Using the app

| Control | What it does |
|---|---|
| **Callsign / Locator** | Your station. Used for the map origin, distances and PSKReporter. |
| **Gain** | Tuner gain in dB, or Auto. Start around 28 dB on HF; lower it if strong broadcast stations cause spurious signals. |
| **ppm** | Frequency correction. The V4 TCXO is ±1 ppm, so 0 is normally fine. |
| **Bias-T** | 4.5 V on the coax for an active antenna or LNA. Leave off for passive antennas. |
| **Invert IQ** | Swaps the spectrum. Not needed with the rtl-sdr-blog driver; kept as a diagnostic. |
| **Save WAV** | Keeps each 15 s slot in `%LOCALAPPDATA%\RtlFt8Skimmer\wav\` (Windows edition). |
| **Slots/band** | 1 = new band every slot (maximum coverage). 2 = each band gets an even and an odd slot back to back. 4 = longer dwell, fewer bands per minute. |
| **jt9** | Path to WSJT-X's `jt9` (`jt9.exe` on Windows, usually `/usr/bin/jt9` on Linux). |
| **PSKReporter / test server** | Upload spots. Keep *test server* ticked until you've confirmed your spots look right. |

Settings are saved to `%LOCALAPPDATA%\RtlFt8Skimmer\settings.json` on Windows and `~/.config/rtlft8skimmer/settings.json` on Linux.

**Coverage trade-off.** With *N* bands selected, each band is heard one slot in *N*. For a dense map of a single band, select only that band; for a propagation overview, select four or five.

## How it works

```mermaid
flowchart LR
    A["RTL-SDR V4<br/>960 kS/s IQ"] --> B["Mix down<br/>offset + 1.5 kHz"]
    B --> C["FIR ÷8<br/>120 kS/s"]
    C --> D["FIR ÷10<br/>12 kS/s"]
    D --> E["Sideband filter<br/>±1.52 kHz"]
    E --> F["USB audio<br/>0–3 kHz"]
    F --> G["15 s WAV"]
    G --> H["jt9 -8"]
    H --> I["Map · list · PSKReporter"]
    S["Band scheduler"] -.->|"retune at 14.0 s"| A
```

1. **Tuning.** The V4 is tuned 150 kHz below the FT8 dial frequency so the DC spike and its neighbourhood stay away from the signal. Below 28.8 MHz the rtl-sdr-blog driver routes the signal through the V4's upconverter automatically.
2. **DSP.** The IQ stream is shifted so the dial frequency plus 1.5 kHz lands at 0 Hz, decimated by 8 and by 10, then passed through a 255-tap low-pass that keeps ±1.52 kHz. Shifting back by +1.5 kHz and taking the real part gives clean upper-sideband audio from 0 to 3 kHz at 12 kS/s, the format `jt9` expects.
3. **Timing.** Every sample carries a UTC timestamp derived from a sample counter, re-anchored to the system clock if USB drops samples. Audio is placed into a 15-second slot buffer by time, not by arrival order.
4. **Band hopping.** At 14.0 s into each slot the receiver retunes to the next band. FT8 transmissions end around 13.2 s, so nothing is lost; the first 150 ms after a retune are discarded while the PLL and filters settle.
5. **Decoding.** Each completed slot is written as a 16-bit WAV and decoded with `jt9 -8 -d 3`. Callsign and locator are extracted from every message (CQ, CQ DX, replies, compound calls), the distance is computed and the spot goes to the map, the list and PSKReporter.

## Building from source

Requirements: .NET 10 SDK. For the Windows app also Visual Studio 2026 with the *.NET desktop development* workload (or just the SDK on Windows).

**Windows, Visual Studio:** open `RtlFt8Skimmer.slnx`, set **Skimmer.Wpf** as the startup project, select the **x64** platform and press F5.

**Windows, command line:**

```
dotnet publish src/Skimmer.Wpf -c Release -r win-x64 --self-contained true -o publish
```

**Linux:**

```
sudo apt install libusb-1.0-0-dev cmake wsjtx
git clone --depth 1 --branch v1.3.6 https://github.com/rtlsdrblog/rtl-sdr-blog
cmake -S rtl-sdr-blog -B rtl-sdr-blog/build -DDETACH_KERNEL_DRIVER=ON && cmake --build rtl-sdr-blog/build --target rtlsdr
dotnet publish src/Skimmer.Web -c Release -r linux-x64 --self-contained true -o publish   # or linux-arm64
cp -L rtl-sdr-blog/build/src/librtlsdr.so.0 publish/
```

`src/Skimmer.Wpf/native/rtlsdr.dll` is copied to the output automatically. To use a newer V4-aware build, replace that file.

**CI.** Every push runs the tests, builds the Windows app and the Linux x64/ARM64 packages, and smoke-tests the Linux server. Publishing a GitHub release attaches all three ready-to-run packages to it.

## Testing without hardware

```
sudo apt install wsjtx
dotnet run -c Release --project tests/Skimmer.Tests
```

The end-to-end test needs no radio. It generates FT8 signals with WSJT-X's `ft8sim`, converts them into 960 kS/s IQ with realistic 8-bit noise on three different bands, and runs them through the full engine: band hopping, retuning, DSP and `jt9`. It checks that:

- every message is decoded on the correct band and audio frequency (±3 Hz), down to −20 dB SNR;
- nothing leaks into a band it wasn't transmitted on;
- an inverted spectrum decodes nothing without Invert IQ and everything with it;
- the band scheduler covers both slot parities for 2, 3 and 4 bands;
- the message parser handles CQ, CQ DX, replies, RR73 and compound calls;
- PSKReporter packets have valid IPFIX headers, set lengths and padding.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| *"rtlsdr.dll is missing"* | The DLL must sit next to `RtlFt8Skimmer.exe`. Only `x64/rtlsdr.dll` from rtl-sdr-blog is needed; libusb is linked into it. |
| *"Entry point not found … libusb_…"* | An older build picked up another program's `rtlsdr.dll` from `PATH`. Current builds load only from the app folder. |
| *"No RTL-SDR found"* (Windows) | Install the WinUSB driver with Zadig, and close any other SDR program that holds the dongle. |
| *"No RTL-SDR found"* (Linux) | Run `sudo ./install.sh` for the udev rule, then unplug and replug the dongle. Make sure `rtl_tcp`, SDR++ or another program isn't using it. |
| *"librtlsdr not found"* (Linux) | The bundled `librtlsdr.so.0` must sit next to `rtlft8skimmer`, or install `librtlsdr2` (2.0.1 or newer supports the V4). |
| *"jt9 not found"* (Linux) | `sudo apt install wsjtx`. |
| Waterfall shows only noise and fixed vertical lines on every band | No HF signal is reaching the V4. Check the antenna, coax and SMA connector; for an active antenna tick Bias-T. Compare with SDR++ on 7.2 MHz AM, where broadcast stations are always present. |
| `0 decodes` every slot although signals are visible | Check the PC clock (±1 s). Try Invert IQ. Tick Save WAV and open a slot in WSJT-X to compare. |
| Only very strong or local stations decode | Weak antenna or high local noise. Move the antenna outdoors and away from PSUs and monitors, or try a longer wire. Check [PSKReporter](https://pskreporter.info/pskmap.html) to see whether nearby stations are hearing more. |
| Map is blank | No internet connection for the map tiles, or (Windows) the WebView2 Runtime is missing. |
| Page says *Disconnected, retrying…* (Linux) | The server stopped or the port is blocked by a firewall: `sudo ufw allow 8073/tcp`. |

## Project layout

```
src/
  Skimmer.Core/        engine, no UI — runs on Windows and Linux
    Native/            P/Invoke and DLL loader for rtlsdr
    Dsp/               FIR decimators, USB demodulator, FFT, waterfall lines
    SkimmerEngine.cs   slot timing, band hopping, decode dispatch
    Bands.cs           FT8 dial frequencies, parity-aware scheduler
    Jt9Decoder.cs      runs jt9, parses its output
    Ft8Message.cs      callsign / locator extraction
    PskReporterClient  IPFIX spot upload
  Skimmer.Wpf/         Windows desktop app (WPF + WebView2/Leaflet map)
    native/rtlsdr.dll  rtl-sdr-blog driver, copied to the output
  Skimmer.Web/         Linux / Raspberry Pi server: REST + WebSocket
    wwwroot/index.html the browser interface, embedded in the executable
packaging/linux/       install.sh, udev rule, systemd unit
tests/
  Skimmer.Tests/       end-to-end test with synthetic IQ
```

## Credits

- FT8 decoding by [WSJT-X](https://wsjt.sourceforge.io/) (K1JT, K9AN, G4WJS and team).
- `rtlsdr.dll` and `librtlsdr.so.0` from [rtl-sdr-blog](https://github.com/rtlsdrblog/rtl-sdr-blog) v1.3.6, GPL-2.0; source at the linked release.
- Map: [Leaflet](https://leafletjs.com/) with © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors.
- Spot network: [PSKReporter](https://pskreporter.info) by Philip Gladstone, N1DQ.

Written by SV1EEX. 73!
