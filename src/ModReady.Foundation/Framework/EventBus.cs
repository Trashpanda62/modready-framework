// ModReady clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// EventBus -- ModReady v2.0 framework primitive #1: type-safe + named-channel
// publish/subscribe for mod-to-mod IPC.
//
// Two ways for one mod to talk to another without a hard assembly reference:
//
//   1. Typed channel (best when both mods reference a SHARED contract assembly
//      that declares the event type T):
//          EventBus.Subscribe<MyEvent>(e => ...);
//          EventBus.Publish(new MyEvent { ... });
//
//   2. Named channel (best when the two mods share NO common type -- the true
//      IPC case, e.g. Diplomacy wants to tell any listener "war declared"
//      without either side referencing the other):
//          EventBus.Subscribe("ModReady.WarDeclared", payload => ...);
//          EventBus.Publish("ModReady.WarDeclared", someObject);
//
// Design rules (why this is safe to drop into a 60 Hz game loop):
//   - A throwing handler is CAUGHT and logged, never allowed to break sibling
//     handlers or bubble into the publisher. One sloppy mod can't take down
//     the bus for everyone.
//   - Dispatch iterates a snapshot, so a handler may subscribe/unsubscribe
//     during its own callback without corrupting the in-flight dispatch or
//     throwing "collection modified".
//   - Subscriptions return an IDisposable token; Dispose() is the unsubscribe.
//     Re-disposing is a no-op. There is also an explicit Unsubscribe overload.
//   - Optional per-subscription throttle: minIntervalMs drops deliveries that
//     arrive faster than the floor (handler sees at most one event per window).
//   - No TaleWorlds dependency -- pure System + nothing else. That keeps the
//     primitive testable off-engine and immune to game-version type drift.

using System;
using System.Collections.Generic;
using System.Linq;

using ModReady.Foundation;   // DiagLog

namespace ModReady.Framework
{
    /// <summary>
    /// Process-wide, thread-safe publish/subscribe bus for mod-to-mod
    /// communication. Static because there is exactly one game process and the
    /// whole point is that mods which don't reference each other can still meet
    /// on a shared channel. See file header for the typed vs named-channel
    /// distinction.
    /// </summary>
    public static class EventBus
    {
        private const string Tag = "ModReady.EventBus";

        // Token handed back from every Subscribe call. Disposing it removes the
        // handler. We keep a back-reference to the owning channel list + the
        // node so Dispose is O(1)-ish (a list removal) and idempotent.
        private sealed class Subscription : IDisposable
        {
            private Action? _remove;
            public Subscription(Action remove) { _remove = remove; }
            public void Dispose()
            {
                // Swap-to-null first so a double Dispose (or a handler that
                // disposes itself mid-dispatch) only unhooks once.
                var r = System.Threading.Interlocked.Exchange(ref _remove, null);
                r?.Invoke();
            }
        }

        // A single registered handler. Boxed object payload so typed and named
        // channels share one dispatch core. Throttle state lives here so it is
        // per-subscription, not per-channel. Plain ctor (no `required`) -- the
        // `required` keyword needs polyfill attributes that don't exist on net472.
        private sealed class Handler
        {
            public readonly Action<object> Invoke;
            public readonly string ChannelKey;
            public readonly long MinIntervalTicks;   // 0 == no throttle
            public long LastDeliveredTicks;          // Stopwatch ticks of last delivery
            public readonly object Owner;            // the user delegate, for Unsubscribe-by-delegate

            public Handler(Action<object> invoke, string channelKey, object owner, long minIntervalTicks)
            {
                Invoke = invoke;
                ChannelKey = channelKey;
                Owner = owner;
                MinIntervalTicks = minIntervalTicks;
                LastDeliveredTicks = 0;
            }
        }

        private static readonly object _gate = new();
        // channelKey -> ordered list of handlers. Typed channels use the type's
        // AssemblyQualifiedName-free key "type:<FullName>"; named channels use
        // "name:<channel>". Keeping both in one dictionary keeps dispatch uniform.
        private static readonly Dictionary<string, List<Handler>> _channels =
            new(StringComparer.Ordinal);

        private static readonly System.Diagnostics.Stopwatch _clock =
            System.Diagnostics.Stopwatch.StartNew();

        // ---- diagnostics counters (lifetime = AppDomain) ----
        private static long _published;
        private static long _delivered;
        private static long _throttled;
        private static long _handlerFaults;

        /// <summary>Total Publish calls across all channels this session.</summary>
        public static long PublishedCount => System.Threading.Interlocked.Read(ref _published);
        /// <summary>Total handler invocations that actually fired this session.</summary>
        public static long DeliveredCount => System.Threading.Interlocked.Read(ref _delivered);
        /// <summary>Deliveries suppressed by a per-subscription throttle.</summary>
        public static long ThrottledCount => System.Threading.Interlocked.Read(ref _throttled);
        /// <summary>Handler invocations that threw and were swallowed+logged.</summary>
        public static long HandlerFaultCount => System.Threading.Interlocked.Read(ref _handlerFaults);

        // ----------------------------------------------------------------
        // Typed channel
        // ----------------------------------------------------------------

        /// <summary>
        /// Subscribe to events of type <typeparamref name="T"/>. Returns a
        /// token; dispose it to unsubscribe. <paramref name="minIntervalMs"/>
        /// &gt; 0 installs a throttle: the handler receives at most one event
        /// per that many milliseconds (excess events are dropped for THIS
        /// subscription only, not the channel).
        /// </summary>
        public static IDisposable Subscribe<T>(Action<T> handler, int minIntervalMs = 0)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Add(TypeKey(typeof(T)), o => handler((T)o), handler, minIntervalMs);
        }

        /// <summary>
        /// Publish an event to every subscriber of its exact runtime type.
        /// Returns the number of handlers that fired (after throttling).
        /// Null events are ignored (returns 0). Never throws on a faulty handler.
        /// </summary>
        public static int Publish<T>(T evt)
        {
            if (evt == null) return 0;
            // Dispatch on the RUNTIME type so a subclass instance reaches
            // subscribers of that subclass. Subscribers of a base type are NOT
            // auto-notified -- exact-type matching keeps the contract obvious
            // and cheap. Cross-mod contracts should publish the concrete type.
            return Dispatch(TypeKey(evt.GetType()), evt!);
        }

        // ----------------------------------------------------------------
        // Named channel (string IPC -- no shared type required)
        // ----------------------------------------------------------------

        /// <summary>
        /// Subscribe to a named string channel. The payload is whatever the
        /// publisher passed (commonly a string, a primitive, or a small DTO the
        /// subscriber inspects by reflection / duck typing). Returns an
        /// unsubscribe token.
        /// </summary>
        public static IDisposable Subscribe(string channel, Action<object> handler, int minIntervalMs = 0)
        {
            if (string.IsNullOrEmpty(channel)) throw new ArgumentException("channel is required", nameof(channel));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Add(NameKey(channel), handler, handler, minIntervalMs);
        }

        /// <summary>
        /// Publish a payload to a named channel. Returns the number of handlers
        /// that fired. A null payload is allowed (named channels are sometimes
        /// pure signals); handlers receive <see cref="String.Empty"/> in that
        /// case so they never have to null-check.
        /// </summary>
        public static int Publish(string channel, object? payload)
        {
            if (string.IsNullOrEmpty(channel)) return 0;
            return Dispatch(NameKey(channel), payload ?? string.Empty);
        }

        // ----------------------------------------------------------------
        // Unsubscribe helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Remove a typed subscription by the original delegate (alternative to
        /// disposing the token). Returns true if a handler was removed.
        /// </summary>
        public static bool Unsubscribe<T>(Action<T> handler)
            => RemoveByOwner(TypeKey(typeof(T)), handler);

        /// <summary>
        /// Remove a named-channel subscription by the original delegate.
        /// Returns true if a handler was removed.
        /// </summary>
        public static bool Unsubscribe(string channel, Action<object> handler)
            => !string.IsNullOrEmpty(channel) && RemoveByOwner(NameKey(channel), handler);

        /// <summary>
        /// Drop every subscription on a channel. Mostly for tests and teardown.
        /// </summary>
        public static void ClearChannel(string channel)
        {
            if (string.IsNullOrEmpty(channel)) return;
            lock (_gate) { _channels.Remove(NameKey(channel)); }
        }

        /// <summary>Remove ALL subscriptions on ALL channels (test teardown).</summary>
        public static void Reset()
        {
            lock (_gate) { _channels.Clear(); }
        }

        /// <summary>Current live subscription count across all channels.</summary>
        public static int SubscriptionCount
        {
            get { lock (_gate) { return _channels.Values.Sum(l => l.Count); } }
        }

        // ----------------------------------------------------------------
        // Core
        // ----------------------------------------------------------------

        private static string TypeKey(Type t) => "type:" + (t.FullName ?? t.Name);
        private static string NameKey(string channel) => "name:" + channel;

        private static IDisposable Add(string key, Action<object> invoke, object owner, int minIntervalMs)
        {
            long minTicks = minIntervalMs > 0
                ? (long)(minIntervalMs * (System.Diagnostics.Stopwatch.Frequency / 1000.0))
                : 0;
            var h = new Handler(invoke, key, owner, minTicks);
            lock (_gate)
            {
                if (!_channels.TryGetValue(key, out var list))
                {
                    list = new List<Handler>(2);
                    _channels[key] = list;
                }
                list.Add(h);
            }
            return new Subscription(() =>
            {
                lock (_gate)
                {
                    if (_channels.TryGetValue(key, out var list))
                    {
                        list.Remove(h);
                        if (list.Count == 0) _channels.Remove(key);
                    }
                }
            });
        }

        private static bool RemoveByOwner(string key, object owner)
        {
            lock (_gate)
            {
                if (!_channels.TryGetValue(key, out var list)) return false;
                int removed = list.RemoveAll(h => ReferenceEquals(h.Owner, owner)
                                              || Equals(h.Owner, owner));
                if (list.Count == 0) _channels.Remove(key);
                return removed > 0;
            }
        }

        private static int Dispatch(string key, object payload)
        {
            System.Threading.Interlocked.Increment(ref _published);

            // Snapshot under lock, invoke outside the lock. Invoking under the
            // lock would deadlock any handler that (re)subscribes, and would
            // serialize the whole bus behind one slow handler.
            Handler[] snapshot;
            lock (_gate)
            {
                if (!_channels.TryGetValue(key, out var list) || list.Count == 0)
                    return 0;
                snapshot = list.ToArray();
            }

            long now = _clock.ElapsedTicks;
            int fired = 0;
            foreach (var h in snapshot)
            {
                if (h.MinIntervalTicks > 0)
                {
                    // Throttle: skip if we're inside the window since last delivery.
                    long last = System.Threading.Interlocked.Read(ref h.LastDeliveredTicks);
                    if (last != 0 && (now - last) < h.MinIntervalTicks)
                    {
                        System.Threading.Interlocked.Increment(ref _throttled);
                        continue;
                    }
                    // CAS the window stamp so the "at most one delivery per window"
                    // contract holds even when two tick threads dispatch the same
                    // channel concurrently: only the thread that wins the swap
                    // delivers; a racing thread that read the same `last` loses
                    // the CAS and is throttled. (Uncontended -> always succeeds,
                    // so single-threaded behavior is unchanged.)
                    if (System.Threading.Interlocked.CompareExchange(
                            ref h.LastDeliveredTicks, now, last) != last)
                    {
                        System.Threading.Interlocked.Increment(ref _throttled);
                        continue;
                    }
                }

                try
                {
                    h.Invoke(payload);
                    fired++;
                    System.Threading.Interlocked.Increment(ref _delivered);
                }
                catch (Exception ex)
                {
                    System.Threading.Interlocked.Increment(ref _handlerFaults);
                    try
                    {
                        DiagLog.LogCaught(Tag,
                            $"handler fault on {key} (payload {payload.GetType().Name})", ex);
                    }
                    catch { /* logging must never poison the bus */ }
                }
            }
            return fired;
        }
    }
}
