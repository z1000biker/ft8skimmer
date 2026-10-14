New in v0.2.0: **Linux and Raspberry Pi edition.**

The same skimmer engine now runs headless on Linux, with the full interface (map, waterfall, decode list, settings) in your browser. Put a Raspberry Pi next to the antenna and watch from any PC or phone on your network.

**Downloads**
- `RtlFt8Skimmer-v0.2.0-win-x64.zip`: Windows desktop app. Unzip and run `RtlFt8Skimmer.exe`.
- `rtlft8skimmer-v0.2.0-linux-x64.tar.gz`: Linux PC.
- `rtlft8skimmer-v0.2.0-linux-arm64.tar.gz`: Raspberry Pi 4/5 with a 64-bit OS.

All packages are self-contained (no .NET installation needed) and include the V4-aware rtlsdr library from rtl-sdr-blog v1.3.6.

**Linux in four commands**
```
sudo apt install wsjtx libusb-1.0-0
tar xzf rtlft8skimmer-v0.2.0-linux-arm64.tar.gz && cd rtlft8skimmer-v0.2.0-linux-arm64
sudo ./install.sh --service
# open http://<pi>:8073/ , enter your callsign and locator, press Start
```

**Also in this release**
- Linux packages include a udev rule, a systemd service and an install script
- Command-line playback of raw IQ recordings (`--iq-file`) for testing without a dongle
- Clearer error messages per operating system

Windows still needs the WinUSB driver (Zadig) and WSJT-X installed. Enter your own callsign and locator before pressing Start.
