using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NewRelic.Api.Agent;
using RyanairPayments.Models;

namespace RyanairPayments.Services
{
    /// <summary>
    /// Thread-safe in-memory payment store instrumented with New Relic custom
    /// attributes, metrics, and custom events for every state transition.
    /// </summary>
    public sealed class PaymentService : IPaymentService
    {
        public static readonly PaymentService Instance = new PaymentService();

        private const int MaxCapacity = 2000;

        private readonly ConcurrentDictionary<Guid, Payment> _store
            = new ConcurrentDictionary<Guid, Payment>();

        private readonly ConcurrentQueue<Guid> _insertionOrder
            = new ConcurrentQueue<Guid>();

        private PaymentService() { }

        public int TotalCount => _store.Count;

        // ─── Create ─────────────────────────────────────────────────────────────

        [Trace]
        public Payment Create(PaymentRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var payment = new Payment
            {
                Id                 = Guid.NewGuid(),
                BookingReference   = request.BookingReference,
                Amount             = Math.Round(request.Amount, 2),
                Currency           = request.Currency ?? "EUR",
                Method             = request.Method,
                Status             = PaymentStatus.Pending,
                Type               = request.Type,
                Origin             = request.Origin?.ToUpperInvariant(),
                Destination        = request.Destination?.ToUpperInvariant(),
                PassengerName      = request.PassengerName,
                PassengerCount     = request.PassengerCount,
                CreatedAt          = DateTime.UtcNow,
                MerchantId         = "RYANAIR-PSP-001",
                AcquirerBank       = PickAcquirer(request.Method),
                CardMasked         = MaskCard(request.CardToken),
                ProcessorReference = $"PSP-{Guid.NewGuid():N}".Substring(0, 20).ToUpperInvariant()
            };

            _store[payment.Id] = payment;
            _insertionOrder.Enqueue(payment.Id);
            EvictIfOverCapacity();

            RecordCreationTelemetry(payment);
            return payment;
        }

        // ─── Read ────────────────────────────────────────────────────────────────

        public Payment GetById(Guid id)
        {
            _store.TryGetValue(id, out var payment);
            return payment;
        }

        public IEnumerable<Payment> GetRecent(int limit = 50)
        {
            if (limit <= 0) limit = 50;
            if (limit > 500) limit = 500;

            return _insertionOrder
                .Reverse()
                .Take(limit)
                .Select(id => _store.TryGetValue(id, out var p) ? p : null)
                .Where(p => p != null)
                .ToList();
        }

        // ─── Status update ───────────────────────────────────────────────────────

        [Trace]
        public bool UpdateStatus(Guid id, PaymentStatus status,
                                 string errorCode = null, string errorMessage = null)
        {
            if (!_store.TryGetValue(id, out var payment)) return false;

            var previousStatus = payment.Status;
            payment.Status       = status;
            payment.ErrorCode    = errorCode;
            payment.ErrorMessage = errorMessage;

            if (status == PaymentStatus.Captured  ||
                status == PaymentStatus.Declined   ||
                status == PaymentStatus.Failed     ||
                status == PaymentStatus.Refunded)
            {
                payment.ProcessedAt = DateTime.UtcNow;
            }

            RecordStatusTransitionTelemetry(payment, previousStatus, errorCode, errorMessage);
            return true;
        }

        // ─── Stats ───────────────────────────────────────────────────────────────

        public PaymentStats GetStats()
        {
            var all = _store.Values.ToList();

            var captured = all.Where(p => p.Status == PaymentStatus.Captured).ToList();
            var declined = all.Where(p => p.Status == PaymentStatus.Declined).ToList();
            var pending  = all.Where(p => p.Status == PaymentStatus.Pending ||
                                          p.Status == PaymentStatus.Authorised).ToList();
            var refunded = all.Where(p => p.Status == PaymentStatus.Refunded).ToList();

            int authorisable = captured.Count + declined.Count;
            double authRate  = authorisable > 0
                ? Math.Round((double)captured.Count / authorisable * 100, 1)
                : 0.0;

            // Emit a real-time auth rate metric every time stats are queried
            NrApi.RecordMetric("Custom/Payments/AuthorisationRate", (float)authRate);
            NrApi.RecordMetric("Custom/Payments/TotalStored",       (float)all.Count);

            return new PaymentStats
            {
                TotalTransactions    = all.Count,
                CapturedTransactions = captured.Count,
                DeclinedTransactions = declined.Count,
                PendingTransactions  = pending.Count,
                RefundedTransactions = refunded.Count,
                TotalAmountCaptured  = Math.Round(captured.Sum(p => p.Amount), 2),
                TotalAmountRefunded  = Math.Round(refunded.Sum(p => p.Amount), 2),
                AuthorisationRate    = authRate,
                ByMethod             = all.GroupBy(p => p.Method.ToString())
                                          .ToDictionary(g => g.Key, g => g.Count()),
                ByType               = all.GroupBy(p => p.Type.ToString())
                                          .ToDictionary(g => g.Key, g => g.Count()),
                ByStatus             = all.GroupBy(p => p.Status.ToString())
                                          .ToDictionary(g => g.Key, g => g.Count()),
                ByRoute              = all.GroupBy(p => p.Route)
                                          .OrderByDescending(g => g.Count())
                                          .Take(10)
                                          .ToDictionary(g => g.Key, g => g.Count()),
                GeneratedAt          = DateTime.UtcNow
            };
        }

        // ─── New Relic telemetry helpers ─────────────────────────────────────────

        private static void RecordCreationTelemetry(Payment payment)
        {
            // Custom attributes on the current transaction / span
            var txn = NrApi.GetAgent().CurrentTransaction;
            txn.AddCustomAttribute("payment.id",              payment.Id.ToString())
               .AddCustomAttribute("payment.bookingReference", payment.BookingReference)
               .AddCustomAttribute("payment.method",           payment.Method.ToString())
               .AddCustomAttribute("payment.type",             payment.Type.ToString())
               .AddCustomAttribute("payment.currency",         payment.Currency)
               .AddCustomAttribute("payment.amount",           (double)payment.Amount)
               .AddCustomAttribute("payment.route",            payment.Route)
               .AddCustomAttribute("payment.origin",           payment.Origin)
               .AddCustomAttribute("payment.destination",      payment.Destination)
               .AddCustomAttribute("payment.passengerCount",   payment.PassengerCount)
               .AddCustomAttribute("payment.acquirerBank",     payment.AcquirerBank)
               .AddCustomAttribute("payment.merchantId",       payment.MerchantId);

            // Dimensional counters — queryable in NRQL as:
            //   SELECT sum(newrelic.timeslice.value) FROM Metric WHERE metricTimesliceName = 'Custom/Payments/Created'
            NrApi.RecordMetric("Custom/Payments/Created",                    1f);
            NrApi.RecordMetric($"Custom/Payments/Method/{payment.Method}",   1f);
            NrApi.RecordMetric($"Custom/Payments/Type/{payment.Type}",       1f);
            NrApi.RecordMetric($"Custom/Payments/Currency/{payment.Currency}", 1f);
            NrApi.RecordMetric($"Custom/Payments/Route/{payment.Origin}_{payment.Destination}", 1f);
            NrApi.RecordMetric("Custom/Payments/Amount/Submitted",           (float)Math.Abs((double)payment.Amount));

            // Custom event — queryable as: SELECT * FROM PaymentCreated
            NrApi.RecordCustomEvent("PaymentCreated", new Dictionary<string, object>
            {
                ["paymentId"]        = payment.Id.ToString(),
                ["bookingReference"] = payment.BookingReference,
                ["amount"]           = (double)payment.Amount,
                ["currency"]         = payment.Currency,
                ["method"]           = payment.Method.ToString(),
                ["type"]             = payment.Type.ToString(),
                ["route"]            = payment.Route,
                ["origin"]           = payment.Origin,
                ["destination"]      = payment.Destination,
                ["passengerCount"]   = payment.PassengerCount,
                ["acquirerBank"]     = payment.AcquirerBank,
                ["merchantId"]       = payment.MerchantId,
                ["processorRef"]     = payment.ProcessorReference,
                ["timestamp"]        = payment.CreatedAt
            });
        }

        private static void RecordStatusTransitionTelemetry(Payment payment,
            PaymentStatus previousStatus, string errorCode, string errorMessage)
        {
            var txn = NrApi.GetAgent().CurrentTransaction;
            txn.AddCustomAttribute("payment.id",             payment.Id.ToString())
               .AddCustomAttribute("payment.status",         payment.Status.ToString())
               .AddCustomAttribute("payment.previousStatus", previousStatus.ToString())
               .AddCustomAttribute("payment.method",         payment.Method.ToString())
               .AddCustomAttribute("payment.type",           payment.Type.ToString())
               .AddCustomAttribute("payment.route",          payment.Route)
               .AddCustomAttribute("payment.currency",       payment.Currency)
               .AddCustomAttribute("payment.amount",         (double)payment.Amount);

            switch (payment.Status)
            {
                case PaymentStatus.Authorised:
                    NrApi.RecordMetric("Custom/Payments/Authorised", 1f);
                    break;

                case PaymentStatus.Captured:
                    NrApi.RecordMetric("Custom/Payments/Captured",              1f);
                    NrApi.RecordMetric("Custom/Payments/Amount/Captured",       (float)payment.Amount);
                    NrApi.RecordMetric($"Custom/Payments/Method/{payment.Method}/Captured", 1f);
                    NrApi.RecordMetric($"Custom/Payments/Route/{payment.Origin}_{payment.Destination}/Captured", 1f);

                    NrApi.RecordCustomEvent("PaymentCaptured", new Dictionary<string, object>
                    {
                        ["paymentId"]        = payment.Id.ToString(),
                        ["bookingReference"] = payment.BookingReference,
                        ["amount"]           = (double)payment.Amount,
                        ["currency"]         = payment.Currency,
                        ["method"]           = payment.Method.ToString(),
                        ["type"]             = payment.Type.ToString(),
                        ["route"]            = payment.Route,
                        ["acquirerBank"]     = payment.AcquirerBank,
                        ["processorRef"]     = payment.ProcessorReference,
                        ["processedAt"]      = payment.ProcessedAt ?? DateTime.UtcNow
                    });
                    break;

                case PaymentStatus.Declined:
                    txn.AddCustomAttribute("payment.errorCode",    errorCode)
                       .AddCustomAttribute("payment.errorMessage", errorMessage);

                    NrApi.RecordMetric("Custom/Payments/Declined", 1f);
                    if (!string.IsNullOrEmpty(errorCode))
                        NrApi.RecordMetric($"Custom/Payments/DeclineReason/{errorCode}", 1f);

                    // Surface declines as noticed errors so they appear in Error Analytics
                    NrApi.NoticeError(
                        errorMessage ?? "Payment declined",
                        new Dictionary<string, string>
                        {
                            ["payment.id"]       = payment.Id.ToString(),
                            ["payment.errorCode"] = errorCode ?? "UNKNOWN",
                            ["payment.method"]   = payment.Method.ToString(),
                            ["payment.route"]    = payment.Route,
                            ["payment.amount"]   = payment.Amount.ToString("F2")
                        });

                    NrApi.RecordCustomEvent("PaymentDeclined", new Dictionary<string, object>
                    {
                        ["paymentId"]        = payment.Id.ToString(),
                        ["bookingReference"] = payment.BookingReference,
                        ["amount"]           = (double)payment.Amount,
                        ["currency"]         = payment.Currency,
                        ["method"]           = payment.Method.ToString(),
                        ["type"]             = payment.Type.ToString(),
                        ["route"]            = payment.Route,
                        ["errorCode"]        = errorCode ?? "UNKNOWN",
                        ["errorMessage"]     = errorMessage ?? string.Empty,
                        ["acquirerBank"]     = payment.AcquirerBank
                    });
                    break;

                case PaymentStatus.Refunded:
                    NrApi.RecordMetric("Custom/Payments/Refunded",        1f);
                    NrApi.RecordMetric("Custom/Payments/Amount/Refunded", (float)Math.Abs((double)payment.Amount));

                    NrApi.RecordCustomEvent("PaymentRefunded", new Dictionary<string, object>
                    {
                        ["paymentId"]        = payment.Id.ToString(),
                        ["bookingReference"] = payment.BookingReference,
                        ["amount"]           = (double)Math.Abs((double)payment.Amount),
                        ["currency"]         = payment.Currency,
                        ["method"]           = payment.Method.ToString(),
                        ["route"]            = payment.Route,
                        ["originalPaymentId"] = payment.OriginalPaymentId?.ToString() ?? string.Empty
                    });
                    break;

                case PaymentStatus.Failed:
                    txn.AddCustomAttribute("payment.errorCode",    errorCode)
                       .AddCustomAttribute("payment.errorMessage", errorMessage);

                    NrApi.RecordMetric("Custom/Payments/Failed", 1f);
                    NrApi.NoticeError(
                        errorMessage ?? "Payment processing failed",
                        new Dictionary<string, string>
                        {
                            ["payment.id"]        = payment.Id.ToString(),
                            ["payment.errorCode"] = errorCode ?? "UNKNOWN",
                            ["payment.route"]     = payment.Route
                        });
                    break;
            }
        }

        // ─── Internal helpers ────────────────────────────────────────────────────

        private void EvictIfOverCapacity()
        {
            while (_store.Count > MaxCapacity && _insertionOrder.TryDequeue(out var oldId))
                _store.TryRemove(oldId, out _);
        }

        private static string PickAcquirer(PaymentMethod method)
        {
            switch (method)
            {
                case PaymentMethod.Visa:
                case PaymentMethod.Mastercard: return "Worldpay";
                case PaymentMethod.Amex:       return "AmexProcessing";
                case PaymentMethod.PayPal:     return "PayPal";
                default:                       return "Adyen";
            }
        }

        private static string MaskCard(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var last4 = token.Length >= 4 ? token.Substring(token.Length - 4) : token;
            return $"****-****-****-{last4}";
        }
    }
}
