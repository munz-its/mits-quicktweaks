using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace MunzQuickTweaks.Core.Tools;

/// <summary>Ein Durchlauf. Fehlende Werte (Test übersprungen oder fehlgeschlagen) sind null.</summary>
public sealed class BenchmarkResult
{
    public DateTimeOffset Time { get; set; }

    public string AppVersion { get; set; } = string.Empty;

    /// <summary>Punkte, ein Kern. Höher ist besser.</summary>
    public double? CpuSingle { get; set; }

    /// <summary>Punkte, alle Kerne.</summary>
    public double? CpuMulti { get; set; }

    /// <summary>Kopierrate des Arbeitsspeichers in GB/s.</summary>
    public double? RamGBs { get; set; }

    public double? DiskWriteMBs { get; set; }

    public double? DiskReadMBs { get; set; }

    /// <summary>Zufällige 4K-Lesezugriffe pro Sekunde.</summary>
    public double? DiskIops { get; set; }

    public string? Drive { get; set; }

    /// <summary>Bilder pro Sekunde im Grafik-Test.</summary>
    public double? GpuFps { get; set; }

    public string? GpuName { get; set; }

    /// <summary>Notiz des Nutzers oder aktiver Zustand, z. B. "Game Mode an".</summary>
    public string? Note { get; set; }
}

/// <summary>Grafik-Test, umgesetzt im Windows-Projekt über DirectX 11.</summary>
public interface IGpuBenchmark
{
    /// <returns>Name der Grafikkarte und Bilder pro Sekunde.</returns>
    (string Name, double Fps) Run(TimeSpan duration, CancellationToken cancel);
}

/// <summary>
/// Prozessor, Arbeitsspeicher und Datenträger. Jeder Test läuft eine feste Zeit und rechnet in eine Rate um,
/// damit schnelle und langsame PCs gleich lange warten.
/// </summary>
public static class Benchmarks
{
    private const int WorkUnit = 20_000;

    /// <summary>Punkte pro Sekunde für einen Thread. Mischung aus Gleitkomma (Mandelbrot) und Ganzzahl (xorshift).</summary>
    public static double CpuSingle(TimeSpan duration, CancellationToken cancel = default)
    {
        var units = RunUnits(duration, 0, cancel);
        return Math.Round(units / duration.TotalSeconds, 1);
    }

    public static double CpuMulti(TimeSpan duration, CancellationToken cancel = default)
    {
        var threads = Environment.ProcessorCount;
        var counts = new long[threads];
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = cancel },
            i => counts[i] = RunUnits(duration, i, cancel));
        return Math.Round(counts.Sum() / duration.TotalSeconds, 1);
    }

    private static long RunUnits(TimeSpan duration, int seed, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew();
        long units = 0;
        double sink = 0;
        while (watch.Elapsed < duration)
        {
            cancel.ThrowIfCancellationRequested();
            sink += WorkOnce(seed + (int)units);
            units++;
        }

        // Ergebnis benutzen, damit der Compiler die Schleife nicht wegoptimiert
        return sink == double.MinValue ? -1 : units;
    }

    private static double WorkOnce(int seed)
    {
        uint x = (uint)seed * 2654435761u + 1;
        double acc = 0;
        for (var i = 0; i < WorkUnit; i++)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            double cr = ((x & 0xFFFF) / 32768.0) - 1.5, ci = (((x >> 16) & 0xFFFF) / 32768.0) - 1.0;
            double zr = 0, zi = 0;
            var n = 0;
            while (n < 16 && zr * zr + zi * zi < 4)
            {
                var t = zr * zr - zi * zi + cr;
                zi = 2 * zr * zi + ci;
                zr = t;
                n++;
            }

            acc += n;
        }

        return acc;
    }

    /// <summary>Kopierrate in GB/s mit 256 MB großen Blöcken, damit kein Cache mitzählt.</summary>
    public static double RamCopy(TimeSpan duration, CancellationToken cancel = default)
    {
        const int size = 256 * 1024 * 1024;
        var source = GC.AllocateUninitializedArray<byte>(size);
        var target = GC.AllocateUninitializedArray<byte>(size);
        Random.Shared.NextBytes(source.AsSpan(0, 4096));
        source.AsSpan().Fill(7);

        // Ein Durchgang zum Aufwärmen, damit die Seiten wirklich im Speicher liegen
        source.AsSpan().CopyTo(target);

        var watch = Stopwatch.StartNew();
        long copied = 0;
        while (watch.Elapsed < duration)
        {
            cancel.ThrowIfCancellationRequested();
            source.AsSpan().CopyTo(target);
            copied += size;
        }

        return Math.Round(copied / watch.Elapsed.TotalSeconds / 1e9, 2);
    }

    /// <summary>
    /// Schreiben und Lesen einer Testdatei ohne Windows-Cache, danach zufällige 4K-Lesezugriffe.
    /// Die Datei wird immer gelöscht, auch bei Abbruch.
    /// </summary>
    public static (double WriteMBs, double ReadMBs, double Iops) Disk(string folder, long fileSize, TimeSpan randomDuration, CancellationToken cancel = default)
    {
        const int block = 4 * 1024 * 1024;
        const int small = 4096;
        var path = Path.Combine(folder, $"munz-benchmark-{Guid.NewGuid():N}.tmp");

        // Ohne Puffer (FILE_FLAG_NO_BUFFERING) misst der Test den Datenträger und nicht den Arbeitsspeicher
        var direct = OperatingSystem.IsWindows() ? (FileOptions)0x20000000 : FileOptions.None;
        fileSize = fileSize / block * block;

        unsafe
        {
            var buffer = (byte*)NativeMemory.AlignedAlloc(block, 4096);
            try
            {
                var span = new Span<byte>(buffer, block);
                Random.Shared.NextBytes(span);

                double write;
                using (var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.WriteThrough | direct, fileSize))
                {
                    var watch = Stopwatch.StartNew();
                    for (long offset = 0; offset < fileSize; offset += block)
                    {
                        cancel.ThrowIfCancellationRequested();
                        RandomAccess.Write(handle, span, offset);
                    }

                    RandomAccess.FlushToDisk(handle);
                    write = fileSize / watch.Elapsed.TotalSeconds / 1e6;
                }

                double read;
                double iops;
                using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None, direct))
                {
                    var watch = Stopwatch.StartNew();
                    for (long offset = 0; offset < fileSize; offset += block)
                    {
                        cancel.ThrowIfCancellationRequested();
                        RandomAccess.Read(handle, span, offset);
                    }

                    read = fileSize / watch.Elapsed.TotalSeconds / 1e6;

                    var smallSpan = new Span<byte>(buffer, small);
                    var slots = fileSize / small;
                    long count = 0;
                    watch.Restart();
                    while (watch.Elapsed < randomDuration)
                    {
                        cancel.ThrowIfCancellationRequested();
                        RandomAccess.Read(handle, smallSpan, Random.Shared.NextInt64(slots) * small);
                        count++;
                    }

                    iops = count / watch.Elapsed.TotalSeconds;
                }

                return (Math.Round(write), Math.Round(read), Math.Round(iops));
            }
            finally
            {
                NativeMemory.AlignedFree(buffer);
                TryDelete(path);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp-Datei bleibt liegen, die Datenträgerbereinigung entfernt sie später
        }
    }

    /// <summary>Veränderung in Prozent gegenüber einem älteren Wert, null wenn einer fehlt.</summary>
    public static double? Change(double? now, double? before) =>
        now is { } n && before is { } b && b > 0 ? Math.Round((n - b) / b * 100, 1) : null;
}

/// <summary>Verlauf der Benchmarks als JSON Lines, für den Vorher-Nachher-Vergleich.</summary>
public sealed class BenchmarkStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _path;

    public BenchmarkStore(string path)
    {
        _path = path;
    }

    public void Add(BenchmarkResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.AppendAllText(_path, JsonSerializer.Serialize(result, Options) + Environment.NewLine);
    }

    /// <summary>Neueste zuerst. Kaputte Zeilen werden übersprungen.</summary>
    public IReadOnlyList<BenchmarkResult> All()
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<BenchmarkResult>();
        }

        var list = new List<BenchmarkResult>();
        foreach (var line in File.ReadLines(_path))
        {
            try
            {
                if (JsonSerializer.Deserialize<BenchmarkResult>(line, Options) is { } r)
                {
                    list.Add(r);
                }
            }
            catch (JsonException)
            {
                // Zeile überspringen
            }
        }

        list.Reverse();
        return list;
    }
}
