// NewRelicStub.cs
//
// Compile-time stub for the New Relic .NET Agent API.
//
// This file is intentionally bundled in the project rather than pulled from NuGet.
// All methods are silent no-ops. When the New Relic .NET agent profiler is attached
// at runtime (via environment variables or MSI), it replaces these implementations
// via IL instrumentation — your custom attributes, metrics, and events will flow
// to New Relic as expected.
//
// To activate:
//   docker run -e NEW_RELIC_LICENSE_KEY=<key> -e NEW_RELIC_APP_NAME=Ryanair-Payments ...

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NewRelic.Api.Agent
{
    // ── Attributes ──────────────────────────────────────────────────────────────

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TransactionAttribute : Attribute
    {
        public bool Web { get; set; } = true;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TraceAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class IgnoreTransactionAttribute : Attribute { }

    // ── Interfaces ───────────────────────────────────────────────────────────────

    public interface ISpan
    {
        ISpan AddCustomAttribute(string key, object value);
    }

    public interface ITransaction
    {
        ITransaction AddCustomAttribute(string key, object value);
        ITransaction SetTransactionName(string category, string name);
        ISpan CurrentSpan { get; }
    }

    public interface IAgent
    {
        ITransaction CurrentTransaction { get; }
        ITransaction StartBackgroundTransaction(string category, string name, Action wrapperAction);
    }

    // ── No-op implementations ────────────────────────────────────────────────────

    internal sealed class NoOpSpan : ISpan
    {
        internal static readonly NoOpSpan Instance = new NoOpSpan();
        public ISpan AddCustomAttribute(string key, object value) => this;
    }

    internal sealed class NoOpTransaction : ITransaction
    {
        internal static readonly NoOpTransaction Instance = new NoOpTransaction();
        public ITransaction AddCustomAttribute(string key, object value) => this;
        public ITransaction SetTransactionName(string category, string name) => this;
        public ISpan CurrentSpan => NoOpSpan.Instance;
    }

    internal sealed class NoOpAgent : IAgent
    {
        internal static readonly NoOpAgent Instance = new NoOpAgent();
        public ITransaction CurrentTransaction => NoOpTransaction.Instance;
        public ITransaction StartBackgroundTransaction(string category, string name, Action wrapperAction)
        {
            wrapperAction?.Invoke();
            return NoOpTransaction.Instance;
        }
    }

    // ── Static entry point (mirrors NewRelic.Api.Agent.NewRelic) ─────────────────

    public static class NewRelic
    {
        public static IAgent GetAgent() => NoOpAgent.Instance;

        public static void RecordMetric(string name, float value) { }

        public static void RecordCustomEvent(string eventType,
            IEnumerable<KeyValuePair<string, object>> attributes) { }

        public static void NoticeError(Exception exception) { }

        public static void NoticeError(Exception exception,
            IDictionary<string, string> customAttributes) { }

        public static void NoticeError(string message,
            IDictionary<string, string> customAttributes) { }

        public static void SetTransactionName(string category, string name) { }

        public static void AddCustomParameter(string key, string value) { }
        public static void AddCustomParameter(string key, IConvertible value) { }
    }
}
