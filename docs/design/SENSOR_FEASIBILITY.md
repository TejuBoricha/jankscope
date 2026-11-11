# Sensor feasibility spike (read-only, user-mode, non-elevated)

Machine: Lenovo 82XV, Windows 11 Home SL 10.0.26300, Intel Core i7-13620H (6P+4E, 16 threads), Intel UHD (iGPU) + NVIDIA RTX 4060 Laptop (dGPU). PowerShell 5.1.
Session: NOT elevated (IsInRole(Administrator) = False). Everything below was run as a standard-privilege process. Nothing was installed, downloaded, written to hardware/settings, or sent off-machine. The Jankscope repository was not touched.
Machine state during probes: AC plugged in, battery ~96%, light/moderate CPU load (about 50% processor utility, from background activity). Values are one-machine, one-moment observations.

Scripts (throwaway): `.../reports/spike/win32power.ps1`, `winrt.ps1`, `battery_ioctl.ps1`. The PDH/CIM/powercfg/nvidia-smi probes were run inline and are not saved as files.

## Result table

Legend: Doc = documented Windows user-mode interface. Vendor = vendor tool / OEM BIOS WMI, not a documented Windows interface. "Elev" = needs elevation (as observed on this machine; "not tested elevated" means unverified).

| # | Interface | Reachable | Elev | Real value returned | Plausible? |
|---|---|---|---|---|---|
| 1 | PDH `Thermal Zone Information\Temperature` / `High Precision Temperature` (Doc) | Yes | No | tz00 = 301 K (27.9 C); HighPrecision 3010 (tenths K). Identical across 14+ samples over ~25 s. `% Passive Limit`=100, `Throttle Reasons`=0 | Reachable but NOT a CPU temperature. Static, one zone `\_tz.TZ00`, 28 C while the CPU draws ~45 W and dGPU reads 63 C. Looks like an ACPI placeholder zone. Do not display as CPU temp. |
| 2 | PDH `Processor Information\% Processor Performance` (Doc) | Yes | No | _Total 121.7%; per-logical 94.7% (E-cores) to 162.7% (P-cores) | Yes. >100% means above nominal (turbo). Relative to the 2400 MHz base. |
| 3 | PDH `Processor Information\Processor Frequency` (Doc) | Yes | No | 2400 MHz on P threads, 1800 MHz on E threads, _Total 2250 | Plausible but this is the nominal/base frequency, not the actual clock. Do not treat as live clock. |
| 4 | PDH `Processor Information\Actual Frequency` (Doc, listed in counter set) | Yes | No | _Total 2986 MHz; cpu0 3155; cpu5 3129; cpu12 (E) 2637 | Yes. Consistent with 2400 MHz base x ~1.2-1.6 performance %. Best live clock source. Per-core, tracks P vs E. |
| 5 | PDH `Processor Information\% Performance Limit`, `Performance Limit Flags` | Yes | No | _Total 93%, flags=2; cpu0 100%, flags=0 | Values returned. Bit meaning of the flags was not verified. Unverified semantics. |
| 6 | PDH `Processor Information\% Processor Utility`, `Parking Status` | Yes | No | _Total utility 53.0; parking 0 on cpu0 and cpu12 | Yes. |
| 7 | PDH `Power Meter\Power` (Doc) | Yes (counter exists) | No | 0 for `Power Meter (0)` and `_Total` | 0 here means "no ACPI power meter"; it is NOT a measured 0 W. Treat as unknown. |
| 8 | PDH `Energy Meter(RAPL_Package0_PKG)\Power` and `\Energy` (Doc) | Yes | No | Package power 42.6 to 48.1 W over 6 s (counter value in mW). Energy counter increments ~12.4e9 to 13.6e9 per second. Cores (PP0) 31.6 to 38.2 W. DRAM = 0 power and 0 energy. PP1 power = 0 (energy counter nonzero but not moving). `_Total` power = 0. | Yes for PKG and PP0. Cross-check: PKG energy delta over 5 s = 63.54e9 pWh = 0.0635 Wh = 228.7 J, divided by 5 s = 45.7 W, matches the Power counter (~45.4 W mean). The energy unit is picowatt-hours (inferred from this check, the counter-set help text does not state it). DRAM/PP1/_Total zeros mean unavailable, not 0 W. **This is real CPU package wattage with no driver and no admin.** |
| 9 | PDH `GPU Adapter Memory` Dedicated/Shared/Total Committed (Doc) | Yes | No | LUID 0x0499259c: dedicated 470 MiB, shared 59 MiB; LUID 0x12a5b: dedicated 0, shared 529 MiB | Yes. LUID-to-GPU mapping was inferred (dGPU dedicated 470 MiB vs nvidia-smi 463 MiB), not read through DXGI. Unverified mapping. |
| 10 | PDH `GPU Engine\Utilization Percentage` (Doc) | Yes (1184 instances) | No | dGPU: 3D 0.4%, Copy 10.8%; iGPU 3D 0.1% | Yes, but per-engine and per-process; must be summed per LUID and engine type. nvidia-smi showed 0% at the same time (it counts only the graphics engine). |
| 11 | PDH `GPU Process Memory` (Doc) | Yes (235 instances) | No | Top process (dGPU) 111.5 MiB dedicated | Yes. Per-PID, useful for "who is using the GPU". |
| 12 | PDH `Battery Status` set | No: no such counter set on this OS (set list had none) | n/a | n/a | n/a |
| 13 | CIM `root/wmi MSAcpi_ThermalZoneTemperature` (Doc) | No | Yes: "Access denied" | none | Not tested elevated, so unverified. Likely the same ACPI zone as row 1, so probably the same placeholder reading. |
| 14 | CIM `Win32_Fan` | Yes (class returns 1 instance) | No | "Cooling Device", ActiveCooling=True, DesiredSpeed empty, VariableSpeed empty | Stub. No RPM. Not usable. |
| 15 | CIM `Win32_TemperatureProbe`, `Win32_VoltageProbe` | Yes (1 instance each) | No | CurrentReading empty; Accuracy/MaxReadable/MinReadable/Resolution/Tolerance all 32768 | Placeholder/garbage. No reading. `Win32_CurrentProbe` returns no instances. |
| 16 | CIM `Win32_Battery` | Yes | No | EstimatedChargeRemaining 95; BatteryStatus 2 (AC); DesignVoltage 17019 mV; EstimatedRunTime 71582788 (sentinel); DesignCapacity/FullChargeCapacity empty | Charge % and voltage plausible. Runtime is a sentinel (AC power), not a number. Capacities are empty here. |
| 17 | CIM `Win32_PortableBattery` | Yes | No | "-Virtual Battery 0-", Location "Fake", ManufactureDate 2010 | Fake/virtual entry. Ignore. |
| 18 | CIM `Win32_VideoController` | Yes | No | Intel UHD (driver 32.0.101.6733, 1920x1080 @144 Hz); NVIDIA RTX 4060 Laptop (driver 32.0.16.2114, AdapterRAM 4293918720) | Identity and driver info fine. AdapterRAM for the NVIDIA card is the 32-bit-capped value (~4 GiB); real VRAM is 8188 MiB per nvidia-smi. Do not trust AdapterRAM. |
| 19 | CIM `root/wmi BatteryStatus` | Yes | No | RemainingCapacity 47060 mWh; Voltage 17018 mV; ChargeRate 0; DischargeRate 0; PowerOnline True | Yes. Rates 0 because on AC and idle-at-96% (not charging). |
| 20 | CIM `root/wmi BatteryFullChargedCapacity` | Yes | No | 49100 mWh | Yes. With design capacity 60000 mWh (row 24) gives health ~81.8%. |
| 21 | CIM `root/wmi BatteryCycleCount` | Yes | No | 213 | Plausible. |
| 22 | CIM `root/wmi BatteryRuntime` | Yes | No | 4294967295 (sentinel, unknown) | Unknown, not a value. |
| 23 | CIM `root/wmi BatteryTemperature` | Class exists, no instances | No | none | Not provided by this battery. |
| 24 | WinRT `Windows.Devices.Power.Battery.AggregateBattery.GetReport()` (Doc) | Yes | No | Status Idle; ChargeRate 0 mW; Design 60000 mWh; Full 49100 mWh; Remaining 47060 mWh | Yes. Only documented source that gave Design capacity here. |
| 25 | WinRT `Windows.System.Power.PowerManager` (Doc) | Yes | No | BatteryStatus Idle; 96%; PowerSupply Adequate; EnergySaver Disabled | Yes. `EffectivePowerMode` printed empty from PowerShell 5.1 (not verified further). |
| 26 | Win32 `GetSystemPowerStatus` (Doc) | Yes | No | AC=1, 96%, lifetime -1 (unknown) | Yes. |
| 27 | Win32 `CallNtPowerInformation(ProcessorInformation)` (Doc) | Yes | No | Max 2400 MHz all threads; Current 2400 on P threads, 1520 on cpu8, 1800 on E threads; limit 1800 on E threads | Reports the nominal tier, not turbo (cpu current never exceeds 2400 while PDH Actual Frequency shows ~3.1 GHz). Use row 4 for live clock instead. |
| 28 | Win32 `CallNtPowerInformation(SystemBatteryState)` (Doc) | Yes | No | AC online, MaxCap 49100, Remaining 47060 mWh, Rate 0 mW, EstTime 0xFFFFFFFF | Consistent with rows 19-24. |
| 29 | Battery class IOCTLs (`IOCTL_BATTERY_QUERY_*`, SetupAPI + DeviceIoControl) (Doc, WDK) | Partly | No | Status: 47060 mWh, 17020 mV, Rate 0 mW. Temperature query failed (Win32 error 1, invalid function). Estimated time sentinel. | Voltage/capacity/rate fine. Battery temperature NOT supported by this battery/driver. Opened with a read+write handle (required by the class driver); only read IOCTLs issued. |
| 30 | WinRT `Windows.Devices.Sensors` (Doc) | Yes (API) | No | Accelerometer, Gyrometer, Compass, LightSensor, Inclinometer, OrientationSensor, Barometer, HumanPresenceSensor: GetDefault() = null. ProximitySensor/HingeAngleSensor: no GetDefault in this projection (not tested via other API). | Absent on this laptop. Important: this API has no thermal, fan, or voltage sensor type at all. |
| 31 | `powercfg /getactivescheme`, `/list`, `/query SCHEME_CURRENT SUB_PROCESSOR` (Doc) | Yes | No | Active: Balanced (381b4222-...). Min processor state AC/DC 5%; Max processor state AC/DC 100% | Yes. Read-only settings; only the first 60 lines of the query were reviewed. Only one scheme listed. |
| 32 | OEM Lenovo WMI `root/wmi LENOVO_GAMEZONE_DATA` (GetFan1Speed, GetFan2Speed, GetCPUTemp, GetGPUTemp, GetFanMaxSpeed, GetGPUPow, GetThermalMode, GetCpuFrequency...) (Vendor/OEM, not a documented Windows interface) | Classes and method names exist; data NOT read | Yes: enumerating instances gave "Access denied"; method calls without an instance failed with "Invalid method Parameter(s)" | none | **Unverified.** The class exposes fan-speed and CPU/GPU-temp getters, so this is where fan RPM and CPU temp could come from on this Lenovo, but it needs elevation (not tested, I was not elevated and did not attempt elevation), is Lenovo-specific, and the return units are unknown. Set* methods exist and were deliberately not touched. `LENOVO_FAN_MAX_SPEED_DATA` read: "Not supported". `Lenovo_BatteryInformation` read: "Access denied". |
| 33 | `nvidia-smi` already installed (`C:\Windows\System32\nvidia-smi.exe`, NVIDIA-SMI 621.14) (Vendor tool, NOT a documented Windows interface) | Yes | No | GPU temp 62.8 to 63.5 C; power draw 13.6 to 15.4 W; enforced power limit 60 W (min 5, max 115); `power.limit` N/A; SM/graphics clock 2010 MHz (max 3105), memory 7001 MHz (max 8001); util 0%; VRAM 463 / 8188 MiB; P-state P3; fan N/A; `temperature.memory` N/A; `temperature.gpu.tlimit` 24; clock event reasons 0x24 (SW power cap and SW thermal slowdown both reported Active); `voltage.graphics` is not a valid field | Temp/power/clock/VRAM plausible and self-consistent across polls. Fan N/A is expected on a laptop (EC-controlled). The "SW thermal slowdown Active" at 0% load and 63 C is as reported; I did not investigate why. Voltage not offered. Separate vendor category by design. |

## What was NOT verified

- Anything that needs elevation (MSAcpi_ThermalZoneTemperature, the Lenovo WMI data/method path). I did not elevate. They may or may not work and may or may not return plausible values elevated.
- The unit and semantics of `Performance Limit Flags` (value 2).
- The exact unit of the Energy Meter `Energy` counter (picowatt-hours is inferred from arithmetic against the Power counter, not documented in the counter help).
- LUID-to-physical-GPU mapping (inferred, not read through DXGI/D3DKMT).
- Behaviour under heavy load, on battery (DC) power, with the dGPU under a real game, or on a different machine. All numbers are single-session, idle-ish, AC-powered observations. Energy Meter on other CPUs (AMD, older Intel) or under a hypervisor was not tested.
- Whether Windows Insider build 10.0.26300 behaves differently from release builds for these counters.
- DXGI `QueryVideoMemoryInfo` and D3DKMT queries: not run.

## Honest assessment

### What a no-driver, observer-only, non-admin tool CAN show (verified here)

- CPU package power in watts and CPU core power (PDH Energy Meter, RAPL-backed). Surprising and valuable: this is the strongest finding. DRAM and PP1 are not available on this machine.
- Per-core actual clock, plus per-core performance %, utility, performance-limit % (PDH Processor Information). P vs E cores are distinguishable by the frequency tier.
- CPU load/utility, parking status.
- GPU engine utilisation (per engine, per process), GPU dedicated/shared memory (per adapter and per process) for both Intel and NVIDIA, via PDH. Vendor-neutral.
- Battery: percent, AC state, voltage, remaining/full/design capacity (health ~82%), cycle count, charge/discharge rate (0 right now), via CIM, WinRT, Win32 and the battery IOCTL. Rate/runtime are sentinels when not discharging, show them as unknown.
- Power plan name and processor min/max state.
- With the NVIDIA driver's own `nvidia-smi` (vendor interface, clearly labelled): dGPU temperature, board power and limit, clocks, VRAM, P-state, throttle reason bitmask.

### What the no-driver path CANNOT show or showed nothing usable for

- **CPU package or per-core temperature:** not available. The only documented thermal source (Thermal Zone PDH, and by inference the MSAcpi WMI class) returned a constant 27.9 C placeholder zone that is clearly not the CPU. A real reading needs MSR access (kernel driver such as the ones LibreHardwareMonitor/HWiNFO use), or the Lenovo OEM WMI route (elevated, vendor-specific, unverified).
- **Fan RPM:** not available. `Win32_Fan` is a stub with no speed; nvidia-smi fan = N/A. The Lenovo `GetFan1Speed`/`GetFan2Speed` methods exist but are admin-gated and unverified. Otherwise needs the embedded controller (kernel driver / OEM service).
- **Voltages (Vcore, rails):** not available. `Win32_VoltageProbe` is a stub; nvidia-smi offers no voltage field. Only battery voltage (17.0 V) is real. Needs a kernel driver (MSR / SMBus / EC).
- **Power limits (PL1/PL2, TDP):** not available from the CPU. Only indirect hints: `% Performance Limit` and `Performance Limit Flags` (semantics unverified), and the dGPU enforced limit (60 W) via nvidia-smi. Reading true PL1/PL2 needs MSR/MMIO (kernel driver) or an OEM/Intel service; the Lenovo `LENOVO_CPU_OVERCLOCKING_DATA` class exists but was not read.
- **dGPU fan/voltage/memory temp:** N/A via nvidia-smi on this laptop.
- **Battery temperature:** the IOCTL returned "invalid function" and the WMI class has no instances.
- **Ambient/lid/accelerometer sensors:** no such hardware reported by Windows.Devices.Sensors; that API has no thermal types anyway.

### Practical recommendation for the product

1. An observer-only "no-driver" mode can honestly ship CPU package watts, actual per-core clocks, performance-limit %, GPU engine/memory, battery health, and (when present) nvidia-smi GPU temp/power/clocks. Mark nvidia-smi data "vendor interface" and degrade gracefully if absent.
2. Show CPU temperature, fan RPM, voltages and power limits as "Unavailable without a kernel driver", not as 0 and not as the 27.9 C thermal-zone value. Detect the static thermal zone (value never changes, or is far below the dGPU while under power) and suppress it.
3. Treat sentinel values (4294967295, 71582788, 32768, 0 from Power Meter) as unknown in the UI.
4. The Lenovo OEM WMI path is the only candidate on this particular laptop for fan RPM and CPU temp without a third-party kernel driver, but it requires elevation and is model-specific. It is worth a separate, clearly labelled, optional, elevated follow-up spike. It was not verified here.

## Elevated follow-up (read-only, run with the owner's approval)

A second pass ran on the same machine, elevated, calling only getters (names starting with `Get` or `Is`) and `Get-CimInstance`. No `Set*` method was called and nothing was written to hardware or settings. The script was a throwaway and is not in the repository.

| Reading | Result, elevated | Assessment |
|---|---|---|
| `MSAcpi_ThermalZoneTemperature` | One zone, 27.9 C, `Active` true | Same constant placeholder as the unelevated Thermal Zone counter. Not a CPU temperature. Suppress it. |
| Lenovo WMI `LENOVO_GAMEZONE_DATA` (needs elevation; unelevated it was Access denied) | `GetCPUTemp` = 80, `GetGPUTemp` = 66 | Plausible: the CPU was drawing about 48 W, and `nvidia-smi` showed 63 C a few minutes earlier. Vendor-specific, one model, no independent reference for the CPU value: unverified. |
| Lenovo WMI fan getters | `GetFanCount` = 0, `GetFan1Speed` = 0, `GetFan2Speed` = 0, `GetFanMaxSpeed` = 0 | Not usable. A laptop has fans, so 0 means "not reported by this route", not "stopped". |
| Lenovo WMI `GetGPUPow` | 0 | Not reported. `nvidia-smi` gave 13.6 to 15.4 W of board power. |
| PDH Energy Meter, elevated | PKG about 48.7 W, PP0 about 38.0 W, PP1 about 0.09 W, DRAM and `_total` 0 | Same as unelevated. Elevation adds nothing here. |

What this changes:

- A CPU temperature is reachable on this Lenovo only through the OEM WMI class, only elevated, and only on models that expose it. It is not a documented Windows interface, and the value was not cross-checked. Treat it as a vendor-specific optional source, never as a baseline.
- Fan RPM is still not reachable, even elevated, with the zero-argument getters. Whether a getter that takes an argument reports it was not tested.
- The conclusion above stands: without a kernel driver, a no-driver observer-only tool gets power, clocks, GPU load and memory, battery data and (through the vendor tool) GPU temperature. On a general machine it does not get CPU temperature, fan RPM, voltages or power limits.

Still unverified after this pass: any other brand's OEM interface, behaviour on battery, under a real game load, and on a non-preview Windows build.
