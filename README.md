# FT8 Skimmer for the RTL-SDR Blog V4

A multiband FT8 monitoring station built around a single RTL-SDR Blog V4. Every 15-second FT8 slot it listens to a different HF band, decodes with WSJT-X's `jt9`, draws the paths on a map and, optionally, reports spots to PSKReporter.

Windows desktop app (WPF, .NET 10) by SV1EEX.

## How it works

1. The V4 runs at 960 kS/s, tuned 150 kHz below the FT8 dial frequency so the DC spike stays out of the way. Below 28.8 MHz the rtl-sdr-blog driver switches on the V4's built-in HF upconverter, so no direct sampling is needed.
2. The IQ stream is mixed down and decimated in three FIR stages to 12 kS/s USB audio (0–3 kHz).
3. At second 14 of each slot the receiver retunes to the next band. FT8 transmissions end around 13.2 s, so nothing is lost.
4. Each slot's audio is written to a WAV file and decoded with `jt9 -8`.
5. Callsign and locator are extracted from each message, the distance is computed and a great-circle path is drawn on the map.

**Slot parity.** FT8 stations transmit in either even or odd slots. With plain round-robin hopping over an even number of bands, every band would always be heard in the same parity and half the stations would be missed. The scheduler shifts the order by one position every round so each band is heard in both parities.

## Requirements (Windows 10/11 x64)

- .NET 10 Desktop Runtime
- WebView2 Runtime (already present on Windows 11)
- WinUSB driver for the dongle, installed with Zadig (same as for SDR# / SDR++)
- WSJT-X installed. The app looks for `jt9.exe` in `C:\WSJT\wsjtx\bin\`; otherwise browse to it with the "…" button
- An HF antenna on the V4 input

`rtlsdr.dll` from rtl-sdr-blog v1.3.6 (`Release.zip` → `x64/rtlsdr.dll`, GPL-2.0, source at <https://github.com/rtlsdrblog/rtl-sdr-blog/releases/tag/v1.3.6>) is included in `src/Skimmer.Wpf/native/` and copied next to the executable on every build. libusb is statically linked into it, so no other DLL is needed. The stock librtlsdr does not know the V4 HF upconverter. The app loads `rtlsdr.dll` only from its own folder, never from `PATH`, so another program's copy cannot be picked up by mistake.

## Building

Visual Studio 2026: open `RtlFt8Skimmer.slnx`, set **Skimmer.Wpf** as startup project, select **x64**, build and run.

Command line:

```
dotnet publish src/Skimmer.Wpf -c Release -o publish
```

The GitHub Actions workflow runs the tests on Linux and produces a ready-to-run Windows package.

## Tests without hardware

```
sudo apt install wsjtx
dotnet run -c Release --project tests/Skimmer.Tests
```

The test generates FT8 signals with `ft8sim`, turns them into 960 kS/s IQ with 8-bit noise on three bands and runs them through the whole engine (band hopping, DSP, `jt9`). It checks that every message is decoded on the right band and audio frequency, that nothing leaks into the wrong band, and that the Invert IQ option recovers an inverted spectrum.

## PSKReporter

Start with "test server" ticked (port 14739). Once spots show up correctly, untick it to report to the live server. Reports are sent every 5 minutes, de-duplicated per band.

## Settings

Stored in `%LOCALAPPDATA%\RtlFt8Skimmer\settings.json`.

| Setting | Meaning |
|---|---|
| Gain | Around 28 dB is a good start on HF. Lower it if strong broadcast stations cause spurious signals. |
| ppm | The V4 TCXO is ±1 ppm, so usually 0. |
| Slots/band | 1 = new band every 15 s. With 2, each band gets an even and an odd slot back to back. |
| Bias-T | Only for an active antenna or LNA powered through the coax. |
| Invert IQ | Swaps the spectrum. Not needed with the rtl-sdr-blog driver; kept as a diagnostic. |
| Save WAV | Keeps each 15 s slot in `%LOCALAPPDATA%\RtlFt8Skimmer\wav` for inspection in WSJT-X. |

## Status

Tested on real hardware (RTL-SDR Blog V4, Windows 11): tuning through the upconverter, slot timing and decoding confirmed on 20 m.
