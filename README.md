# Hi3Helper.Sophon - Sophon Download Library
### Now Available on [NuGet!](https://www.nuget.org/packages/Hi3Helper.Sophon/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Hi3Helper.Sophon.svg?style=flat-square)](https://www.nuget.org/packages/Hi3Helper.Sophon/) [![NuGet version](https://img.shields.io/nuget/v/Hi3Helper.Sophon.svg?style=flat-square)](https://www.nuget.org/packages/Hi3Helper.Sophon/)

**Hi3Helper.Sophon** is a **Sophon-compatible** library to download certain well-known games created by miHoYo/HoYoverse (for example: [**Genshin Impact**](https://genshin.hoyoverse.com/en/home) and [**Zenless Zone Zero**](https://zenless.hoyoverse.com/main)). **Hi3Helper.Sophon** was written and compatible for .NET implementations listed below:
* .NET Standard 2.0 (including .NET Framework 4.6.2, 4.7, 4.7.1, 4.7.2, 4.8 and 4.8.1)
* .NET 6.0
* .NET 8.0
* .NET 9.0

This project is heavily used as a part of our main project's submodule, [**Collapse Launcher**](https://github.com/CollapseLauncher/Collapse).

# What is Sophon?

**Sophon** is a new download method introduced by HoYoverse and was firstly introduced widely to the public on the release of their new Unified launcher, [**HoYoPlay**](https://hoyoplay.hoyoverse.com/).

**Sophon** allows files to be downloaded into **several chunks**, which result more efficient, faster and less error-prone download process compared to conventional archive file download method. This also allows the download process requiring much less disk space since the files will be written into disk on-the-fly without reserving double the size of disk space.

# Usage Example
The Test projects was targetted for multiple TFMs, including .NET Framework 4.6.2, .NET 6.0, .NET 8.0 and .NET 9.0. Below are some example you can try:
### Basic Example
* [**Basic Example: Fetch and Dump Manifest Information to JSON**](Test/SophonManifestInfo2Json/Program.cs)
### Complete Code Example
* [**Complete Code Example: Sophon Download**](Test/SophonDownload/Program.cs)
* [**Complete Code Example: Sophon Update and Preload Chunk Download**](Test/SophonUpdatePreload/Program.cs)
* [**Collapse Launcher's Sophon Code Implementation**](https://github.com/CollapseLauncher/Collapse/blob/main/CollapseLauncher/Classes/InstallManagement/BaseClass/InstallManagerBase.Sophon.cs)