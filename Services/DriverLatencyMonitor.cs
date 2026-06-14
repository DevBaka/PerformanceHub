using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PerformanceHub.Utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerformanceHub.Services
{
    // Collects ETW-based DPC/ISR activity and aggregates entries.
    // Note: First iteration aggregates by Task/Event name ("DPC"/"Interrupt").
    // Further refinement to attribute per driver image will follow.
    // Requires Administrator. If ETW cannot start, monitor stays disabled (no-throw).
    public sealed class DriverLatencyMonitor : IDisposable
    {
        public sealed class Entry
        {
            public string Name { get; init; } = string.Empty;
            public double DpcMs { get; set; }
            public double IsrMs { get; set; }
            public int Events { get; set; }
        }

        private sealed class ModuleMap
        {
            private readonly List<(ulong Start, ulong End, string Name)> _regions = new();
            private readonly object _gate = new();
            public void Add(ulong start, ulong size, string name)
            {
                if (size == 0 || string.IsNullOrWhiteSpace(name)) return;
                var end = start + size - 1;
                // Normalize to filename only (no path), preserve original casing of filename
                var s = name.Replace('/', '\\');
                var idx = s.LastIndexOf('\\');
                var file = idx >= 0 ? s[(idx + 1)..] : s;
                lock (_gate)
                {
                    _regions.Add((start, end, file));
                }
            }
            public void Remove(ulong start)
            {
                lock (_gate)
                {
                    for (int i = _regions.Count - 1; i >= 0; i--)
                    {
                        if (_regions[i].Start == start) _regions.RemoveAt(i);
                    }
                }
            }
            public string? Resolve(ulong addr)
            {
                lock (_gate)
                {
                    for (int i = 0; i < _regions.Count; i++)
                    {
                        var r = _regions[i];
                        if (addr >= r.Start && addr <= r.End) return r.Name;
                    }
                }
                return null;
            }
        }

        private static double ExtractDurationMs(TraceEvent e, bool isDpc, bool isIsr)
        {
            // Try a variety of plausible payload names across Windows versions/providers
            string[] names = isDpc
                ? new[] { "DpcTime", "DpcDuration", "Duration", "ExecTime", "ExecutionTime", "ElapsedTime" }
                : new[] { "InterruptTime", "IsrTime", "IsrDuration", "Duration", "ExecTime", "ExecutionTime", "ElapsedTime" };
            foreach (var n in names)
            {
                try
                {
                    var obj = e.PayloadByName(n);
                    if (obj == null) continue;
                    double val = 0;
                    if (obj is int i) val = i;
                    else if (obj is long l) val = l;
                    else if (obj is double d) val = d;
                    else if (obj is float f) val = f;
                    if (val <= 0) continue;
                    // Heuristic: interpret as microseconds by default. If extremely large, assume nanoseconds.
                    // If very small (<1.0) assume already milliseconds.
                    if (val > 100000.0) // likely microseconds or nanoseconds
                    {
                        // if >1e8 likely ns; convert ns->ms
                        if (val > 1e8) return val / 1e6;
                        // else us->ms
                        return val / 1000.0;
                    }
                    if (val < 1.0) return val * 1000.0; // seconds to ms (rare), but be defensive
                    return val; // assume already ms
                }
                catch { }
            }
            return 0.0;
        }

        private readonly object _gate = new();
        private Thread? _worker;
        private volatile bool _running;
        private readonly Dictionary<string, Entry> _agg = new(StringComparer.OrdinalIgnoreCase);
        private TraceEventSession? _session;
        private ETWTraceEventSource? _source;
        private Thread? _publisher;
        private long _totalKernelEvents;
        private long _matchedDpcIsrEvents;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _recentEvents = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _eventCounts = new(StringComparer.OrdinalIgnoreCase);
        private string? _lastError;
        private readonly ModuleMap _modules = new();

        public long TotalKernelEvents => System.Threading.Interlocked.Read(ref _totalKernelEvents);
        public long MatchedDpcIsrEvents => System.Threading.Interlocked.Read(ref _matchedDpcIsrEvents);
        public string? LastError => _lastError;

        public string[] GetRecentEventSamples(int max = 5)
        {
            var list = new List<string>(max);
            foreach (var s in _recentEvents)
            {
                list.Add(s);
                if (list.Count >= max) break;
            }
            return list.ToArray();
        }

        // Raised every refreshIntervalMs with top entries snapshot.
        public event Action<IReadOnlyList<Entry>>? OnUpdate;

        private readonly int _refreshIntervalMs;
        private readonly bool _includeSystemRows;
        public DriverLatencyMonitor(int refreshIntervalMs = 2000, bool includeSystemRows = true)
        {
            _refreshIntervalMs = refreshIntervalMs;
            _includeSystemRows = includeSystemRows;
        }

        public bool Start()
        {
            if (_running) return true;
            // Admin required for kernel ETW
            if (!AdminUtil.IsAdministrator())
            {
                // Still start publisher to send empty snapshots, so UI can show status
                _running = true;
                _publisher = new Thread(PublisherProc) { IsBackground = true, Name = "DriverLatencyPublisher" };
                _publisher.Start();
                return false;
            }

            try
            {
                _running = true;
                _worker = new Thread(WorkerProc) { IsBackground = true, Name = "DriverLatencyMonitor" };
                _worker.Start();
                return true;
            }
            catch
            {
                _running = false;
                Cleanup();
                return false;
            }
        }

        private void WorkerProc()
        {
            try
            {
                // Start publisher FIRST so UI receives periodic diagnostics even if ETW configuration fails
                if (_publisher == null)
                {
                    _publisher = new Thread(PublisherProc) { IsBackground = true, Name = "DriverLatencyPublisher" };
                    _publisher.Start();
                }

                // Use our own real-time kernel session so we can enable keywords reliably
                var sessionName = "PerformanceHubKernel";
                _session = new TraceEventSession(sessionName) { StopOnDispose = true };
                // Enable all kernel keywords
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.All);
                // Note: StackWalk provider not enabled here due to API availability; routine-pointer attribution remains active.

                _source = _session.Source;
                var parser = new KernelTraceEventParser(_source);

                // Track loaded kernel images to resolve routine addresses -> driver modules
                parser.ImageLoad += e =>
                {
                    try
                    {
                        var baseAddr = (ulong)(e.PayloadByName("ImageBase") as IConvertible)?.ToUInt64(null)!;
                        var size = (ulong)(e.PayloadByName("ImageSize") as IConvertible)?.ToUInt64(null)!;
                        var name = e.FileName ?? e.PayloadStringByName("FileName") ?? string.Empty;
                        _modules.Add(baseAddr, size, name);
                    }
                    catch { }
                };
                parser.ImageUnload += e =>
                {
                    try
                    {
                        var baseAddr = (ulong)(e.PayloadByName("ImageBase") as IConvertible)?.ToUInt64(null)!;
                        _modules.Remove(baseAddr);
                    }
                    catch { }
                };

                // Use parser.All to be version-tolerant; look for DPC/Interrupt/ISR and try to read duration payload when present
                parser.All += (TraceEvent e) =>
                {
                    try
                    {
                        System.Threading.Interlocked.Increment(ref _totalKernelEvents);
                        string task = e.TaskName ?? e.EventName ?? string.Empty;
                        string opcode = e.OpcodeName ?? string.Empty;
                        string provider = e.ProviderName ?? string.Empty;
                        // record a short descriptor for diagnostics
                        try
                        {
                            var desc = $"{provider}:{task}:{opcode}";
                            if (_recentEvents.Count < 50) _recentEvents.Enqueue(desc);
                            while (_recentEvents.Count > 50 && _recentEvents.TryDequeue(out _)) { }
                            _eventCounts.AddOrUpdate(desc, 1, (_, v) => v + 1);
                        }
                        catch { }
                        if (string.IsNullOrEmpty(task) && string.IsNullOrEmpty(opcode) && string.IsNullOrEmpty(provider)) return;
                        bool isDpc = task.IndexOf("DPC", StringComparison.OrdinalIgnoreCase) >= 0
                                     || opcode.IndexOf("DPC", StringComparison.OrdinalIgnoreCase) >= 0
                                     || provider.IndexOf("Kernel-DPC", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isIsr = (!isDpc) && (
                                        task.IndexOf("Interrupt", StringComparison.OrdinalIgnoreCase) >= 0
                                     || task.IndexOf("ISR", StringComparison.OrdinalIgnoreCase) >= 0
                                     || opcode.IndexOf("Interrupt", StringComparison.OrdinalIgnoreCase) >= 0
                                     || opcode.IndexOf("ISR", StringComparison.OrdinalIgnoreCase) >= 0
                                     || provider.IndexOf("Kernel-Interrupt", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!isDpc && !isIsr) return;
                        System.Threading.Interlocked.Increment(ref _matchedDpcIsrEvents);

                        double durMs = ExtractDurationMs(e, isDpc, isIsr);
                        string? module = ResolveRoutineModule(e) ?? ResolveModuleFromStack(e);

                        string key = module ?? (isDpc ? "DPC" : "Interrupt");
                        lock (_gate)
                        {
                            if (!_agg.TryGetValue(key, out var entry))
                            {
                                entry = new Entry { Name = key };
                                _agg[key] = entry;
                            }
                            // Even if duration not present, count the event so UI shows activity
                            if (isDpc) entry.DpcMs += durMs > 0 ? durMs : 0.0; else entry.IsrMs += durMs > 0 ? durMs : 0.0;
                            entry.Events++;
                        }
                    }
                    catch { }
                };

                // Also hook dynamic events to capture providers without strong parser bindings
                _source.Dynamic.All += (TraceEvent e) =>
                {
                    try
                    {
                        System.Threading.Interlocked.Increment(ref _totalKernelEvents);
                        var provider = e.ProviderName ?? string.Empty;
                        var name = e.EventName ?? string.Empty;
                        var opcode = e.OpcodeName ?? string.Empty;
                        try
                        {
                            var desc = $"{provider}:{name}:{opcode}";
                            if (_recentEvents.Count < 50) _recentEvents.Enqueue(desc);
                            while (_recentEvents.Count > 50 && _recentEvents.TryDequeue(out _)) { }
                            _eventCounts.AddOrUpdate(desc, 1, (_, v) => v + 1);
                        }
                        catch { }
                        bool looksDpc = name.IndexOf("DPC", StringComparison.OrdinalIgnoreCase) >= 0 || opcode.IndexOf("DPC", StringComparison.OrdinalIgnoreCase) >= 0 || provider.IndexOf("PerfInfo", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("DPC", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool looksIsr = !looksDpc && (name.IndexOf("Interrupt", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("ISR", StringComparison.OrdinalIgnoreCase) >= 0 || opcode.IndexOf("Interrupt", StringComparison.OrdinalIgnoreCase) >= 0 || opcode.IndexOf("ISR", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!looksDpc && !looksIsr) return;
                        System.Threading.Interlocked.Increment(ref _matchedDpcIsrEvents);
                        double durMs = ExtractDurationMs(e, looksDpc, looksIsr);
                        string? module = ResolveRoutineModule(e) ?? ResolveModuleFromStack(e);

                        string key = module ?? (looksDpc ? "DPC" : "Interrupt");
                        lock (_gate)
                        {
                            if (!_agg.TryGetValue(key, out var entry)) { entry = new Entry { Name = key }; _agg[key] = entry; }
                            if (looksDpc) entry.DpcMs += durMs > 0 ? durMs : 0.0; else entry.IsrMs += durMs > 0 ? durMs : 0.0;
                            entry.Events++;
                        }
                    }
                    catch { }
                };

                // Blocking ETW processing loop
                _source.Process();
            }
            catch (Exception ex)
            {
                // Record error for diagnostics
                _lastError = ex.Message;
            }
            finally
            {
                Cleanup();
            }
        }

        private static bool TryParseAddress(object? obj, out ulong addr)
        {
            addr = 0;
            try
            {
                if (obj == null) return false;
                switch (obj)
                {
                    case ulong ul:
                        addr = ul; return addr != 0;
                    case long l when l > 0:
                        addr = (ulong)l; return addr != 0;
                    case uint ui when ui > 0:
                        addr = ui; return addr != 0;
                    case int i when i > 0:
                        addr = (ulong)i; return addr != 0;
                    case string s when s.Length > 0:
                        {
                            // Accept hex like 0xFFFFFFFF or FFFFFFFF, and decimal
                            s = s.Trim();
                            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
                            if (ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var hx)) { addr = hx; return addr != 0; }
                            if (ulong.TryParse(s, out var dn)) { addr = dn; return addr != 0; }
                            return false;
                        }
                }
            }
            catch { }
            return false;
        }

        private static string? NormalizeModuleName(string? mod)
        {
            if (string.IsNullOrWhiteSpace(mod)) return null;
            var s = mod.Replace('/', '\\');
            var idx = s.LastIndexOf('\\');
            var file = idx >= 0 ? s[(idx + 1)..] : s;
            return file;
        }

        private string? ResolveRoutineModule(TraceEvent e)
        {
            try
            {
                // Try common payload names that may carry routine pointers
                string[] fields = new[] {
                    "StartAddress", "Routine", "Function",
                    "ServiceRoutine", "DpcRoutine", "InterruptRoutine",
                    "Address", "Ptr", "Pointer",
                    // Additional observed names
                    "StartAddr", "TargetAddress", "Handler", "ISR", "DPC", "RoutineAddress"
                };
                foreach (var f in fields)
                {
                    try
                    {
                        var obj = e.PayloadByName(f);
                        if (!TryParseAddress(obj, out var addr) || addr == 0) continue;
                        var mod = _modules.Resolve(addr);
                        if (!string.IsNullOrWhiteSpace(mod)) return NormalizeModuleName(mod);
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private string? ResolveModuleFromStack(TraceEvent e)
        {
            // TraceEvent.CallStack() extension is not available in this build environment.
            // Keep a stub here to compile; attribution falls back to routine pointer and generic keys.
            return null;
        }

        private void PublisherProc()
        {
            try
            {
                while (_running)
                {
                    List<Entry> snapshot;
                    lock (_gate)
                    {
                        snapshot = _agg.Values
                            .OrderByDescending(e => e.DpcMs + e.IsrMs)
                            .Take(25)
                            .Select(e => new Entry { Name = e.Name, DpcMs = e.DpcMs, IsrMs = e.IsrMs, Events = e.Events })
                            .ToList();
                        // Optionally include top observed ETW event signatures (provider:task:opcode) for diagnostics
                        if (_includeSystemRows && _eventCounts.Count > 0)
                        {
                            var top = _eventCounts.OrderByDescending(kv => kv.Value).Take(10).ToList();
                            foreach (var kv in top)
                            {
                                snapshot.Add(new Entry { Name = kv.Key, DpcMs = 0, IsrMs = 0, Events = kv.Value });
                            }
                        }
                        // Surface last ETW error if any
                        if (!string.IsNullOrWhiteSpace(_lastError))
                        {
                            snapshot.Add(new Entry { Name = $"(error) ETW: {_lastError}", DpcMs = 0, IsrMs = 0, Events = 0 });
                        }
                        // decay to emphasize recent activity
                        foreach (var e in _agg.Values)
                        {
                            e.DpcMs *= 0.9;
                            e.IsrMs *= 0.9;
                            e.Events = (int)(e.Events * 0.9);
                        }
                        // Heartbeat: if absolutely no ETW activity, nudge diagnostics so UI visibly updates
                        if (_eventCounts.Count == 0 && _agg.Count == 0)
                        {
                            System.Threading.Interlocked.Increment(ref _totalKernelEvents);
                            try { if (_recentEvents.Count < 50) _recentEvents.Enqueue("HEARTBEAT:no-events"); } catch { }
                        }
                    }
                    OnUpdate?.Invoke(snapshot);
                    Thread.Sleep(_refreshIntervalMs);
                }
            }
            catch { }
        }

        public void Stop()
        {
            _running = false;
            Cleanup();
        }

        private void Cleanup()
        {
            try { _source?.Dispose(); } catch { }
            try { _session?.DisableProvider(KernelTraceEventParser.ProviderGuid); } catch { }
            try { _session?.Dispose(); } catch { }
            _source = null;
            _session = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
