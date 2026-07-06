# DCS Mission Briefing Reader

**DCS Mission Briefing Reader** is a freeware Windows application designed to read and visualize `.miz` mission files from Digital Combat Simulator (DCS World).

![DCS Mission Briefing Reader](DCSMissionReader/Resources/DCS_MBR_Icon.ico)

## Features

- **Mission Parsing:** Extracts briefing text, mission details, and taskings from DCS `.miz` files.
- **Dynamic Map View:** Visualizes mission elements (units, routes, shapes) on an interactive OpenStreetMap-based map.
- **Theater Support:** Caucasus, Syria, Persian Gulf, Nevada, Marianas, South Atlantic, Sinai, Kola, and Afghanistan.

## Installation / Usage

1. Download the latest release.
2. Run `DCSMissionReader.exe`.
3. Click **DCS MISSIONS FOLDER** and select a folder containing `.miz` files.
4. The app automatically indexes all missions and displays them grouped by Theater/Unit.

## Requirements

- Windows 10/11
- .NET 9.0 Runtime

## Development

This is a WPF application built with C# and .NET 9.

### Build

```
dotnet restore
dotnet build
dotnet test
```

### External Libraries

- [GMap.NET](https://github.com/judero01col/GMap.NET) - Map control and tile management.
- [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/) - Local search index.

## Attributions & Legal

- **Disclaimer:** THIS MATERIAL IS NOT MADE OR SUPPORTED BY EAGLE DYNAMICS SA.
- **Map Data:** Map data &copy; [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors.
- **License:** This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---
Created by **Ioannis Roussos** (Jan 2026)
