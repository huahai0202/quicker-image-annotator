# Render Benchmark

| case | image | canvas | actual_backend | hardware_accelerated | avg_ms | p50_ms | p95_ms | max_ms |
| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: |
| 1080p | 1920x1080 | 1280x720 | GpuRenderer | yes | 2.863 | 2.364 | 3.278 | 28.536 |
| 4k | 3840x2160 | 1600x900 | GpuRenderer | yes | 4.541 | 4.389 | 5.449 | 9.974 |
| 8k | 7680x4320 | 1600x900 | GpuRenderer | yes | 14.021 | 13.888 | 15.541 | 17.461 |
| long-shot | 1440x6400 | 900x1400 | GpuRenderer | yes | 4.902 | 4.783 | 5.909 | 7.954 |
