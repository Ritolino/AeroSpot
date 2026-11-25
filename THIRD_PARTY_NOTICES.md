# Third-Party Software Notices and Information

This project, **AeroSpot**, incorporates or links against third-party open-source software libraries.
This document contains the required copyright notices and license texts for these components.

---

## 1. Microsoft.Data.Sqlite & SQLite

- **Component:** `Microsoft.Data.Sqlite`
- **Author:** Microsoft Corporation
- **License:** MIT License
- **Project URL:** https://github.com/dotnet/efcore

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- **Underlying SQLite Engine:** SQLite is in the **Public Domain** (https://www.sqlite.org/copyright.html).
- **SQLitePCLRaw Provider:** Apache License 2.0, Copyright (c) Eric Sink / SourceGear.

---

## 2. Microsoft.ML.OnnxRuntime & DirectML

- **Component:** `Microsoft.ML.OnnxRuntime.DirectML`
- **Author:** Microsoft Corporation
- **License:** MIT License
- **Project URL:** https://github.com/microsoft/onnxruntime

```
The MIT License (MIT)

Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- **DirectML:** DirectML is a DirectX 12 hardware acceleration library provided by Microsoft under the DirectML redistribution terms and MIT License.

---

## 3. SharpVectors (SVG Vector Graphics for WPF)

- **Component:** `SharpVectors.Wpf`
- **Author:** Elinam LLC / Paul Tingey
- **License:** BSD 3-Clause License
- **Project URL:** https://github.com/ElinamLLC/SharpVectors

```
Copyright (c) 2010-2026, Paul Tingey, Elinam LLC
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of Elinam LLC nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

---

## 4. Build & Packaging Tools (Non-Distributed)

- **Inno Setup:** Used optionally as a build-time compiler tool to generate the Windows Installer (`Setup.exe`). Inno Setup is Copyright (c) 1997-2026 Jordan Russell and Martijn Laan. It is not bundled as an application runtime dependency.
- **MSTest:** Used strictly for automated unit and integration tests (`AeroSpot.Tests`). Distributed under the MIT License by Microsoft Corporation.

---

## 5. FFMpegCore & FFmpeg (Out-of-Process Media Transcoding & Probing)

- **Component:** `FFMpegCore` & `FFMpegInstaller.Windows.x64` (`0.1.10`)
- **Author:** Tobias Haimerl / FFMpegCore Contributors
- **License:** MIT License
- **Project URL:** https://github.com/rosenbjerg/FFMpegCore

```
MIT License

Copyright (c) 2018 Tobias Haimerl

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- **Underlying FFmpeg Binaries (`ffmpeg.exe`, `ffprobe.exe` via `FFMpegInstaller.Windows.x64`):**
  - **License:** GNU Lesser General Public License (LGPL) version 2.1 or later.
  - **Project URL:** https://ffmpeg.org
  - **Architecture Compliance:** AeroSpot does not link against FFmpeg C/C++ libraries or incorporate FFmpeg object code. FFmpeg binaries are bundled as standalone CLI executables via `FFMpegInstaller.Windows.x64` and executed strictly out-of-process (`Process.Start`) for container audio conversion and metadata probing.

---

## 6. Docnet.Core & Google PDFium (Native High-Speed PDF Engine)

- **Component:** `Docnet.Core`
- **Author:** Michał Rusin / Docnet Contributors
- **License:** MIT License
- **Project URL:** https://github.com/Greem/Docnet
- **Underlying Engine:** Google PDFium (Chromium Project, Copyright (c) 2014 PDFium Authors, BSD 3-Clause License)
- **Usage:** Used for instant sub-50ms PDF preview, continuous vertical scrolling, left sidebar page thumbnails, interactive hand-tool panning, cursor-anchored zoom, and lightweight in-process memory footprint without spawning browser subprocesses.

---

## 7. PDFsharp (MIT Edition - PDF Merging)

- **Component:** `PDFsharp` (`6.2.4`)
- **Author:** empira Software GmbH
- **License:** MIT License
- **Project URL:** https://github.com/empira/PDFsharp
- **Usage:** Used in `AeroSpot.Worker` and `AeroSpot.Tools` (moved out of `AeroSpot.Core`) for lossless multi-file PDF merging (`--merge-pdf`) and document/image PDF export.


