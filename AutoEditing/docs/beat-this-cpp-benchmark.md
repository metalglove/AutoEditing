# `beat_this_cpp` evaluation

Evaluated 2026-07-21 against the three configured songs in `C:\VEGAS\edit`.
The comparison used fresh output from the current analyzer and
`beat_this_cpp` commit `07ab790` in MSVC Release/CPU mode. This is an agreement
study, not an accuracy benchmark: manually annotated beat truth is not yet
available.

## Result

The Beat This model is promising as an additional beat-grid signal, but this C++
port should not be vendored or adopted directly yet.

| Song | Mode | Current / candidate beats | Within 70 ms | Median nearest error | Current / candidate tempo |
|---|---:|---:|---:|---:|---:|
| Demon Eyes | minimal | 346 / 287 | 24.3% | 149.8 ms | 121.15 / 90.91 BPM |
| Demon Eyes | DBN | 346 / 259 | 21.7% | 167.3 ms | 121.15 / 90.91 BPM |
| Highway Saints | minimal | 301 / 544 | 42.2% | 85.8 ms | 83.87 / 166.67 BPM |
| Highway Saints | DBN | 301 / 544 | 42.5% | 85.3 ms | 83.87 / 166.67 BPM |
| Traveller | minimal | 416 / 591 | 38.9% | 121.6 ms | 94.61 / 142.86 BPM |
| Traveller | DBN | 416 / 617 | 38.0% | 112.1 ms | 94.61 / 142.86 BPM |

The Highway Saints result is especially relevant: an external tempo listing
places it near 168 BPM, suggesting Beat This corrected a half-time error in the
current detector. The other tempo disagreements cannot be judged without manual
annotation. Downbeat phase and meter disagree substantially across both systems
and likewise require ground truth.

## Performance and packaging

Current analysis completed in 0.32–0.68 seconds per song. Beat This took
2.86–3.98 seconds in minimal mode and 3.36–4.75 seconds with DBN. This is roughly
6–10 times slower, but still interactive. Its distribution adds about 90 MB:
79.2 MB model, 10.3 MB ONNX Runtime, and a 0.7 MB executable.

## Engineering findings

The repository did not build and behave as documented on Windows. The isolated
benchmark clone required four temporary fixes:

- add the missing `<array>` include;
- declare the implemented Boolean DBN constructor in the header;
- parse and forward the documented `--dbn` option;
- statically link the benchmark executable to work around broken DLL/miniaudio
  symbol exports.

Its CMake download of ONNX Runtime also lacks a checksum. The port and its main
dependencies use permissive licenses, but their notices would need to be
retained.

## Recommendation

Keep the current region and editorial analyzer. Evaluate Beat This as a
replaceable, out-of-process beat candidate provider, using a maintained runtime
or a small corrected fork rather than coupling the application to this port.
Before making it the default, manually annotate 10–20 representative songs and
measure beat/downbeat F-measure plus continuity and octave metrics. The raw
benchmark artifacts remain under `.tmp/beat-this-benchmark/results` and are not
production dependencies.
