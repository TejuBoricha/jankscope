# Fact register

Spec §1 #8: every dated or externally sourced claim is a claim until verified from an official source. Columns: claim · source · date checked · result. Result values: `verified` (read from the primary source or observed on the bench), `reported` (found only via search snippets or secondary sources — do not rely on it for a decision), `open` (not checked yet), `contradicted`. Checked 2026-10-16 unless stated.

## Toolchain and platform
| # | Claim | Source | Result |
|---|---|---|---|
| F1 | winget package `Microsoft.DotNet.SDK.10` is version 10.0.402; installer hash is verified by winget | `winget show`, install output; SHA-256 930a215d2bcb3154c1898bd0fb9083a5a7755e8ccc26985309b144556557851a | verified |
| F2 | .NET 8 and .NET 9 reach end of support on 2026-11-10; .NET 10 is LTS released 2025-11-11 | Microsoft .NET blog post on .NET 8/9 end of support (devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/), via search | reported; exact .NET 10 end date not confirmed (Microsoft says November 2028; one tracker says the 14th) |
| F3 | Smart App Control state lives at `HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState` (0 off, 1 on, 2 evaluation) | Read on the bench: value 0 | value observed; meaning of 1 and 2 is **open** (informally documented) |
| F4 | A system allows at most 64 concurrent ETW sessions | The probe enumerates with a 64-slot array; the bench has 38 running | open (Microsoft documentation not re-read); consistent with observation |
| F5 | Real-time ETW session creation needs Performance Log Users membership or admin | PresentMon README; bench: unelevated `StartTraceW` succeeded with the group in the token | verified (documentation + observation) |
| F6 | `IsDeviceRegisteredWithManagement` is the right MDM check; the `HKLM\SOFTWARE\Microsoft\Enrollments` key is not | Bench: 30+ enrollment subkeys incl. built-in "Cloud/Local/Deploy Authority" on an unmanaged PC while the API returned "not registered" | verified (observed); regression-tested |
| F7 | SMBIOS chassis type numbering used in `SmbiosChassis` (3 Desktop, 9 Laptop, 10 Notebook, 24 Sealed-Case PC, 31 Convertible, 35 Mini PC, 36 Stick PC) | Microsoft `Win32_SystemEnclosure` reference and DMTF DSP0134 section 7.4.1, via search 2026-10-16 | verified for those values; the code had type 24 mapped to "server" from memory — **a bug, fixed** after the Stage 0 review (see `reviews/stage-0.md`). The full table was not read from the DMTF PDF itself. Re-checked 2026-10-16: Microsoft's `Win32_SystemEnclosure` page documents only types 1–24 and 30–32; types 25–29 and 33–36 (used in `SmbiosChassis` and its table test) follow SMBIOS numbering and are **open** |
| F8 | `runas /trustlevel` can run a program with a Basic User token for restricted-token tests | Microsoft runas reference (via search) | reported; `runas /showtrustlevels` printed nothing in the non-interactive session, so the exact value is **open** |
| F9 | WinUI 3 lacks a built-in tray icon, single-file publish, and has an elevation-launch problem on 24H2 | v3 spec text | open; does not block ADR 0001 (WPF chosen on other grounds) |
| F10 | Windows 10 22H2 is build 19045; Windows 11 is build ≥ 22000 | Standard build numbers; bench reports 26300 | reported |

## PresentMon (frame capture, ADR 0003)
| # | Claim | Source | Result |
|---|---|---|---|
| F11 | License is MIT, copyright Intel Corporation 2017–2024 | github.com/GameTechDev/PresentMon README | verified |
| F12 | Latest release is v2.6.0, published 2026-09-21 (not a pre-release). Assets: `PresentMon-2.6.0-x64.exe` (980,320 bytes, SHA-256 `b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af`), `PresentMon-2.6.0.msi` (169,672,704 bytes), `ReleaseSymbols.zip`. v2.5.0's download was removed (users pointed to 2.5.1) | GitHub REST API `repos/GameTechDev/PresentMon/releases/latest` (digest field) and the releases page, 2026-10-16 | verified; downloaded 2026-10-16 with the owner's yes, **our own SHA-256 matches** the published digest, size matches, Authenticode valid (Intel Corporation) |
| F13 | Console options exist: `--output_stdout`, `--process_name`, `--exclude`, `--exclude_dropped`, `--session_name`, `--stop_existing_session`, `--terminate_existing_session`, `--terminate_on_proc_exit`, `--timed`, `--terminate_after_timed`, `--no_console_stats`, `--output_file`, `--v1_metrics`, `--hotkey`, `--track_gpu_video`, `--restart_as_admin` | README-ConsoleApplication.md, then the pinned v2.6.0 binary's own `--help` (87 lines), 2026-10-16 | verified against the binary |
| F14 | Without admin or Performance Log Users, capture fails with access denied; other users' and short-lived processes show as `<unknown>` | PresentMon README | verified |
| F15 | The console exe is the simplest path from .NET; the service/C-API path has no official .NET wrapper | v3 spec | open |

## Funding, signing, distribution (ADR 0004, 0007)
| # | Claim | Source | Result |
|---|---|---|---|
| F16 | GitHub Sponsors supports India; personal sponsorships carry no GitHub fee; organization sponsorships up to 6% | docs.github.com Sponsors "about" page (fetched) | verified; payout/withdrawal fees and timing **open** |
| F17 | Sponsors requires a bank account and tax residence in India | GitHub changelog, via search | reported |
| F18 | Azure Artifact Signing: individuals only in US/Canada; Indian organizations not on the list | Microsoft Learn quickstart/FAQ, via search | reported; re-check at Stage 5 |
| F19 | SignPath Foundation free OSS signing: OSI license, verifiable automated build, code-signing policy page, MFA, manual release approval, "verifiable reputation" at SignPath's discretion | signpath.org/terms (fetched) | verified; whether a new project qualifies is **open** |
| F20 | Microsoft Store: individual registration free (Sept 2025), company fee waived (May 2026); Microsoft signs store MSIX packages | Windows Developer Blog posts, via search | reported |
| F21 | Store revenue share for non-game apps (85/15, 95/5 with deep links, or 15% when using Microsoft commerce) | conflicting search results | open |
| F22 | Whether a packaged (MSIX) app can launch an elevated helper | not researched | open |
| F23 | Files app is MIT, free on GitHub, paid (about $8–10) in the Store | search results; prices conflict | reported |
| F24 | Certum open-source code signing kit around €69 plus a hardware token; India availability unknown | shop.certum.eu, via search | reported |
| F25 | Ko-fi tips carry no platform fee; Stripe availability for India unconfirmed | Ko-fi help/pricing via search | reported |
| F26 | Open Collective (Open Source Collective) takes 10% plus processing; payouts via PayPal/Wise | docs.opencollective.com via search | reported |

## Observed on the bench during the Stage 0 review fixes (2026-10-16)
| # | Claim | Source | Result |
|---|---|---|---|
| F27 | `QueryAllTracesW` called with an array smaller than the number of running sessions returns `ERROR_MORE_DATA` (234) and reports the required count in `LoggerCount`, so one retry with that count succeeds | `EtwTests.Enumeration_retries_when_the_first_buffer_is_too_small`: capacity 1 against 39 running sessions; the retry-disabled mutation fails it | verified (observed); Microsoft documentation not re-read |
| F29 | In `BannedSymbols.txt`, a METHOD entry only bans the overload whose documentation ID matches exactly: `M:System.Diagnostics.Process.Start` (no parameter list) banned nothing and the build stayed green. Type entries (`T:`) ban every member and every use, including through an alias | Planted `Process.Start`, `GetProcessById`, `Type.GetMethod`, `RegistryKey.CreateSubKey` calls in a scratch copy: none failed until the full signature (or the type) was listed; then each did | verified (observed); Microsoft documentation not re-read |
| F30 | The `LibraryImport` source generator emits `[DllImport]` (`System.Runtime.InteropServices.DllImportAttribute`) into `obj/.../LibraryImports.g.cs`, and the banned-API analyzer does not skip generated code, so `T:System.Runtime.InteropServices.DllImportAttribute` in `BannedSymbols.txt` fails every build that has a LibraryImport | Observed: the first build after adding the entry failed with RS0030 at `LibraryImports.g.cs(58,10)`; the isolated coverage project could not see it because it has no LibraryImport | verified (observed) |
| F28 | A non-elevated filtered-admin token can open the Service Control Manager with `SC_MANAGER_CONNECT` and a service with `SERVICE_QUERY_STATUS` and read the state of a kernel-driver service: `vgk` (system-start) reads running, `EasyAntiCheat_EOS` (demand-start) reads stopped | `jankscope probe` on the bench | verified for this token only; a true standard-user token is untested (L9, L15) |

## Stage 2 Input, Network and Platform Research (Verified 2026-11-04)
| # | Claim | Source | Result |
|---|---|---|---|
| F31 | Kernel-PnP event IDs for device lifecycle: 400 (configure attempted), 410 (configure succeeded), 411 (failed to start / driver fault), 420 (deleted / removed), 430 (surprise removal / sleep transition) | Windows Event Log provider `Microsoft-Windows-Kernel-PnP` schema and MSDN driver documentation | verified |
| F32 | Network latency probing endpoints: Commercial speed-test engines (Ookla, Cloudflare) require proprietary commercial licensing. Unprivileged ICMP Echo to the local default gateway and standard DNS query to root DNS resolvers (1.1.1.1 / 8.8.8.8) are RFC-standard, license-free, and require zero user data transmission | RFC 792 (ICMP), RFC 1035 (DNS), ADR 0009 | verified |
| F33 | Windows 11 Wi-Fi BSSID access: `WlanGetNetworkBssList` on Windows 11 22H2+ requires Windows Location privacy permission ("Let desktop apps access your location"); if disabled, the API returns `ERROR_ACCESS_DENIED` | Windows Dev Center documentation and Windows 11 location privacy policy | verified |
| F34 | Public repository handle: Reserved and active on GitHub | `https://github.com/TejuBoricha/jankscope`, verified live via git remote and GitHub API | verified |
| F35 | `DwmSetWindowAttribute` with attribute 20 (`DWMWA_USE_IMMERSIVE_DARK_MODE`) draws the title bar of the program's own window dark; Windows 10 20H1 (build 19041) and later know it, an older build refuses it and the title bar stays light | Bench, 2026-11-13, Windows 11 build 26300: Windows returned success for 1 and for 0 on a real window handle, and `DwmGetWindowAttribute` read 1 back from the running app's main window; the title bar was seen dark in a capture of that window | verified on Windows 11 only; the build 19041 floor is **reported** from memory of the header, not re-read from Microsoft documentation, and Windows 10 was not run (`LIMITATIONS` L5). A code review noted that on Windows 10 a title bar that is already on screen may not repaint after a theme switch until the window is resized or reactivated; not observed, because Windows 11 repaints at once, and no second native import was added to force it |

## Not yet researched (gate later stages)
Azure Windows-client licensing (P1) · WHEA/storage event IDs (Stage 3) · TraceEvent / ScottPlot / Direct3D binding licenses (adoption time).
