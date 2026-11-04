# Overhead Measurement v1: Observer-Effect Paired A-B-A-B Benchmark

Per **spec section 14** (Self-overhead and self-protection) and **section 18** (Statistical rigor: 10+ interleaved runs with median and IQR):
The observer-effect benchmark compares identical workloads run with collectors **OFF** (condition A) versus collectors **ON** (condition B) across 5 interleaved A/B pairs (10 runs total), measured directly from the harness's independent QPC timestamps at Present() return.

## Benchmark Configuration
- **Hardware Bench:** Intel UHD Graphics + NVIDIA GeForce RTX 4060 Laptop GPU (10 cores / 16 threads, Windows 11 Home 26300)
- **Workload:** Direct3D 11 flip-model swapchain (DXGI_SWAP_EFFECT_FLIP_DISCARD), 300 frames @ 144 fps with 0.1 ms synthetic jitter per run (5 pairs = 10 interleaved runs)
- **Collector Condition B:** Pinned PresentMon console 2.6.0 running in ETW capture mode

## Results Summary (Median and IQR Across 5 Pairs)

| Metric | Condition A (Off, median / IQR) | Condition B (On, median / IQR) | Observer Delta (B - A) | Limit (section 14) | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Present() Return Error (Median)** | 0.057 ms (IQR 0.013) | 0.066 ms (IQR 0.014) | 0.009 ms | < 0.200 ms | **PASS** |
| **Present() Return Error (p99)** | 0.175 ms (IQR 0.061) | 0.163 ms (IQR 0.028) | -0.012 ms | < 0.500 ms | **PASS** |
| **Frame Time Error (Median)** | 0.013 ms (IQR 0.002) | 0.015 ms (IQR 0.003) | 0.002 ms | < 0.100 ms | **PASS** |

## Individual Run Details (10 Interleaved Runs)

| Run | Collectors | Present() Error (Median / p99) | Frame Time Error (Median / p99) | Disturbed Frames |
| :--- | :--- | :--- | :--- | :--- |
| **A1** | OFF | 0.055 ms / 0.175 ms | 0.013 ms / 0.133 ms | 0 |
| **B1** | ON | 0.063 ms / 0.154 ms | 0.012 ms / 0.114 ms | 0 |
| **A2** | OFF | 0.069 ms / 0.127 ms | 0.011 ms / 0.076 ms | 0 |
| **B2** | ON | 0.096 ms / 0.291 ms | 0.027 ms / 0.220 ms | 0 |
| **A3** | OFF | 0.068 ms / 0.212 ms | 0.018 ms / 0.178 ms | 0 |
| **B3** | ON | 0.066 ms / 0.140 ms | 0.012 ms / 0.066 ms | 0 |
| **A4** | OFF | 0.057 ms / 0.149 ms | 0.015 ms / 0.130 ms | 0 |
| **B4** | ON | 0.065 ms / 0.182 ms | 0.016 ms / 0.161 ms | 0 |
| **A5** | OFF | 0.054 ms / 0.210 ms | 0.013 ms / 0.232 ms | 0 |
| **B5** | ON | 0.080 ms / 0.163 ms | 0.015 ms / 0.094 ms | 0 |

## Stage 1b Desktop App (`jankscope-app.exe`) Self-Overhead & Zero-Network Verification (HC-4 / HC-6)

| Metric | Measured Value | Budget (HC-4 / HC-6) | Status |
| :--- | :--- | :--- | :--- |
| **`jankscope-app.exe` CPU Usage (Idle / Live Capture)** | `0.682%` of total logical CPU | `<= 1.0%` | **PASS** |
| **`jankscope-app.exe` Working Set (RAM)** | `198.9 MB` | `<= 250 MB` | **PASS** |
| **Active TCP / UDP Endpoints (`Get-NetTCPConnection` / `Get-NetUDPEndpoint`)** | `0 TCP / 0 UDP` | `0` (Zero network capability) | **PASS** |

## Conclusion
Real-time ETW event capture and the Stage 1b WPF desktop shell passed all observer-overhead, CPU, memory, and zero-network budgets across 10 interleaved runs (5 A/B pairs).
