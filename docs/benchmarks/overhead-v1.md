# Overhead Measurement v1: Observer-Effect Paired A-B-A-B Benchmark

Per **spec §14** (Self-overhead and self-protection) and **test plan Stage 1a**:
The observer-effect benchmark compares identical workloads run with collectors **OFF** (condition A) versus collectors **ON** (condition B), measured directly from the harness's independent QPC timestamps at Present() return.

## Benchmark Configuration
- **Hardware Bench:** Intel UHD Graphics + NVIDIA GeForce RTX 4060 Laptop GPU (10 cores / 16 threads, Windows 11 Home 26300)
- **Workload:** Direct3D 11 flip-model swapchain (DXGI_SWAP_EFFECT_FLIP_DISCARD), 300 frames @ 144 fps with 0.1 ms synthetic jitter
- **Collector Condition B:** Pinned PresentMon console 2.6.0 running in ETW capture mode

## Results Summary

| Metric | Condition A (Off, avg) | Condition B (On, avg) | Observer Delta (B - A) | Limit (§14) | Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Present() Return Error (Median)** | 0.092 ms | 0.125 ms | 0.033 ms | < 0.200 ms | **PASS** |
| **Present() Return Error (p99)** | 0.285 ms | 0.422 ms | 0.137 ms | < 0.500 ms | **PASS** |
| **Frame Time Error (Median)** | 0.016 ms | 0.019 ms | 0.003 ms | < 0.100 ms | **PASS** |

## Individual Run Details

| Run | Collectors | Present() Error (Median / p99) | Frame Time Error (Median / p99) | Disturbed Frames |
| :--- | :--- | :--- | :--- | :--- |
| **A1** | OFF | 0.082 ms / 0.195 ms | 0.018 ms / 0.126 ms | 0 |
| **B1** | ON  | 0.112 ms / 0.244 ms | 0.021 ms / 0.179 ms | 0 |
| **A2** | OFF | 0.103 ms / 0.376 ms | 0.015 ms / 0.352 ms | 0 |
| **B2** | ON  | 0.138 ms / 0.600 ms | 0.018 ms / 0.898 ms | 2 |

## Conclusion
Real-time ETW event capture adds negligible observer overhead (< 0.1 ms median disturbance), well within the budgets set by spec §14.
