```

BenchmarkDotNet v0.13.12, Ubuntu 25.04 (Plucky Puffin)
Intel Xeon Processor, 1 CPU, 4 logical and 4 physical cores
.NET SDK 6.0.428
  [Host]     : .NET 6.0.36 (6.0.3624.51421), X64 RyuJIT AVX2
  DefaultJob : .NET 6.0.36 (6.0.3624.51421), X64 RyuJIT AVX2


```
| Method                     | Mean     | Error    | StdDev   | Allocated |
|--------------------------- |---------:|---------:|---------:|----------:|
| LookupCandidates_ZeroAlloc | 12.80 ms | 0.067 ms | 0.063 ms |      18 B |
