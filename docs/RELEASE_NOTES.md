First public release of RtlFt8Skimmer, a multiband FT8 skimmer for the RTL-SDR Blog V4.

**Download** `RtlFt8Skimmer-v0.1.0-win-x64.zip`, unzip anywhere and run `RtlFt8Skimmer.exe`. The .NET runtime and the V4-aware `rtlsdr.dll` are included.

**You still need**
- WinUSB driver for the dongle (Zadig), as for SDR# or SDR++
- WSJT-X installed (the app uses its `jt9.exe` decoder)
- WebView2 Runtime (already on Windows 11)

**Highlights**
- One RTL-SDR V4 covers 160 m to 6 m, hopping band every 15 s FT8 slot
- Even/odd slot rotation so no band loses half of its stations
- Live great-circle map, waterfall, decode list, stations-per-band counter
- PSKReporter upload (test and live servers)
- Invert IQ and Save WAV diagnostics

Enter your own callsign and locator before pressing Start.
