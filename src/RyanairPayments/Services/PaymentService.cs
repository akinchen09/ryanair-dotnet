using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RyanairPayments.Models;

namespace RyanairPayments.Services
{
    /// <summary>
    /// Thread-safe in-memory payment store. Acts as a singleton via <see cref="Instance"/>.
    /// Caps storage at <see cref="MaxCapacity"/> entries with a rolling eviction policy.
    /// </summary>
    public sealed class PaymentService : IPaymentService
    {
        public static readonly PaymentService Instance = new PaymentService();

        private const int MaxCapacity = 2000;

        // Primary index: O(1) lookup by ID
        private readonly ConcurrentDictionary<Guid, Payment> _store
            = new ConcurrentDictionary<Guid, Payment>();

        // Insertion-order queue for GetRecent() and eviction
        private readonly ConcurrentQueue<Guid> _insertionOrder
            = new ConcurrentQueue<Guid>();

        private PaymentService() { }

        public int TotalCount => _store.Count;

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
            return payment;
        }

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

        public bool UpdateStatus(Guid id, PaymentStatus status,
                                 string errorCode = null, string errorMessage = null)
        {
            if (!_store.TryGetValue(id, out var payment)) return false;

            payment.Status      = status;
            payment.ErrorCode   = errorCode;
            payment.ErrorMessage = errorMessage;

            if (status == PaymentStatus.Captured ||
                status == PaymentStatus.Declined  ||
                status == PaymentStatus.Failed    ||
                status == PaymentStatus.Refunded)
            {
                payment.ProcessedAt = DateTime.UtcNow;
            }

            return true;
        }

        public PaymentStats GetStats()
        {
            var all = _store.Values.ToList();

            var byStatus = all
                .GroupBy(p => p.Status.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            var captured = all.Where(p => p.Status == PaymentStatus.Captured).ToList();
            var declined = all.Where(p => p.Status == PaymentStatus.Declined).ToList();
            var pending  = all.Where(p => p.Status == PaymentStatus.Pending ||
                                          p.Status == PaymentStatus.Authorised).ToList();
            var refunded = all.Where(p => p.Status == PaymentStatus.Refunded).ToList();

            int authorisable = captured.Count + declined.Count;
            double authRate  = authorisable > 0
                ? Math.Round((double)captured.Count / authorisable * 100, 1)
                : 0.0;

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
                ByStatus             = byStatus,
                ByRoute              = all.GroupBy(p => p.Route)
                                          .OrderByDescending(g => g.Count())
                                          .Take(10)
                                          .ToDictionary(g => g.Key, g => g.Count()),
                GeneratedAt          = DateTime.UtcNow
            };
        }

        private void EvictIfOverCapacity()
        {
            while (_store.Count > MaxCapacity && _insertionOrder.TryDequeue(out var oldId))
            {
                _store.TryRemove(oldId, out _);
            }
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
            var last4 = token.Length >= 4
                ? token.Substring(token.Length - 4)
                : token;
            return $"****-****-****-{last4}";
        }
    }
}
