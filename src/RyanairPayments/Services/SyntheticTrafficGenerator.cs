using System;
using System.Threading;
using RyanairPayments.Models;

namespace RyanairPayments.Services
{
    /// <summary>
    /// Background timer that continuously submits synthetic airline payment transactions
    /// to simulate realistic PSP traffic. Each tick creates 1-3 payments and then
    /// asynchronously resolves their status (pending → authorised → captured/declined).
    /// </summary>
    public sealed class SyntheticTrafficGenerator : IDisposable
    {
        private readonly IPaymentService _paymentService;
        private          Timer           _timer;
        private          Timer           _statusTimer;
        private readonly Random          _rng = new Random();
        private volatile bool            _disposed;

        // Tick interval bounds (ms)
        private const int MinIntervalMs = 2500;
        private const int MaxIntervalMs = 7000;

        // ─── Static data pools ──────────────────────────────────────────────────

        private static readonly (string Origin, string Destination, string Currency, decimal BaseAmount)[] Routes =
        {
            ("DUB", "STN", "EUR", 39.99m),
            ("DUB", "LHR", "GBP", 54.99m),
            ("DUB", "EDI", "GBP", 49.99m),
            ("DUB", "BCN", "EUR", 79.99m),
            ("DUB", "MAD", "EUR", 84.99m),
            ("DUB", "FCO", "EUR", 89.99m),
            ("DUB", "MXP", "EUR", 92.99m),
            ("DUB", "AMS", "EUR", 74.99m),
            ("STN", "BCN", "EUR", 44.99m),
            ("STN", "MAD", "EUR", 49.99m),
            ("STN", "FCO", "EUR", 54.99m),
            ("STN", "CIA", "EUR", 52.99m),
            ("STN", "PMI", "EUR", 39.99m),
            ("STN", "AGP", "EUR", 44.99m),
            ("STN", "CDG", "EUR", 59.99m),
            ("BCN", "MAD", "EUR", 29.99m),
            ("BCN", "LIS", "EUR", 49.99m),
            ("MAD", "LIS", "EUR", 39.99m),
            ("MAD", "MXP", "EUR", 64.99m),
            ("WRO", "STN", "GBP", 34.99m),
            ("KRK", "STN", "GBP", 29.99m),
            ("WRO", "DUB", "EUR", 44.99m),
            ("KRK", "DUB", "EUR", 49.99m),
        };

        private static readonly string[] PassengerNames =
        {
            "James Murphy",      "Sarah O'Brien",    "Michael Flynn",    "Emma Walsh",
            "Patrick Doyle",     "Aoife Kelly",      "Sean Ryan",        "Niamh Burke",
            "Conor McCarthy",    "Ciara O'Sullivan", "Liam Doherty",     "Siobhan Brennan",
            "Declan Fitzgerald", "Fiona Collins",    "Eoin O'Connor",    "Roisin Murray",
            "Tomasz Kowalski",   "Anna Nowak",       "Piotr Wisniewski", "Katarzyna Wójcik",
            "Carlos García",     "Maria López",      "José Martínez",    "Carmen Fernández",
            "Giovanni Rossi",    "Maria Russo",      "Francesca Ricci",  "Alessandro Marino",
        };

        private static readonly PaymentMethod[] Methods =
        {
            PaymentMethod.Visa, PaymentMethod.Visa, PaymentMethod.Visa,
            PaymentMethod.Mastercard, PaymentMethod.Mastercard,
            PaymentMethod.Amex,
            PaymentMethod.PayPal,
            PaymentMethod.ApplePay,
            PaymentMethod.GooglePay,
        };

        private static readonly string[] AncillaryDescriptions =
        {
            "Priority Boarding", "Checked Baggage 20kg", "Checked Baggage 10kg",
            "Seat Selection", "Plus Fare Upgrade", "Flexi Plus Upgrade", "Extra Legroom Seat"
        };

        private static readonly (string Code, string Message)[] DeclineReasons =
        {
            ("DO_NOT_HONOUR",      "Card declined by issuer"),
            ("INSUFFICIENT_FUNDS", "Insufficient funds"),
            ("CARD_EXPIRED",       "Card has expired"),
            ("INVALID_CVV",        "CVV verification failed"),
            ("VELOCITY_EXCEEDED",  "Transaction velocity limit exceeded"),
            ("FRAUD_SUSPECTED",    "Transaction flagged by fraud engine"),
        };

        // ─── Lifecycle ──────────────────────────────────────────────────────────

        public SyntheticTrafficGenerator(IPaymentService paymentService)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        }

        public void Start()
        {
            _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(NextInterval()));
            // Secondary timer resolves pending payments to a terminal state
            _statusTimer = new Timer(OnStatusTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(4));
            Console.WriteLine("[SyntheticTraffic] Generator started.");
        }

        public void Stop()
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _statusTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            Console.WriteLine("[SyntheticTraffic] Generator stopped.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _statusTimer?.Dispose();
        }

        // ─── Tick handlers ──────────────────────────────────────────────────────

        private void OnTick(object state)
        {
            if (_disposed) return;

            int count = _rng.Next(1, 4);
            for (int i = 0; i < count; i++)
            {
                try   { EmitPayment(); }
                catch (Exception ex) { Console.WriteLine($"[SyntheticTraffic] Error emitting payment: {ex.Message}"); }
            }

            // Reschedule with a fresh random interval
            _timer?.Change(NextInterval(), Timeout.Infinite);
        }

        private void OnStatusTick(object state)
        {
            if (_disposed) return;

            // Grab any pending payments and resolve them
            var pending = _paymentService.GetRecent(200);
            foreach (var p in pending)
            {
                if (p.Status != PaymentStatus.Pending && p.Status != PaymentStatus.Authorised)
                    continue;
                if ((DateTime.UtcNow - p.CreatedAt).TotalSeconds < 3)
                    continue;

                try   { ResolvePayment(p.Id); }
                catch (Exception ex) { Console.WriteLine($"[SyntheticTraffic] Error resolving {p.Id}: {ex.Message}"); }
            }
        }

        // ─── Payment generation ─────────────────────────────────────────────────

        private void EmitPayment()
        {
            int roll = _rng.Next(100);
            PaymentType type;
            if      (roll < 60) type = PaymentType.FlightBooking;
            else if (roll < 85) type = PaymentType.Ancillary;
            else if (roll < 95) type = PaymentType.Refund;
            else                type = PaymentType.Amendment;

            var route    = Routes[_rng.Next(Routes.Length)];
            int pax      = _rng.Next(1, Math.Min(5, 10));
            decimal amt  = CalculateAmount(type, route.BaseAmount, pax);
            string  name = PassengerNames[_rng.Next(PassengerNames.Length)];
            string  bref = GenerateBookingReference();

            var request = new PaymentRequest
            {
                BookingReference = bref,
                Amount           = amt,
                Currency         = route.Currency,
                Method           = Methods[_rng.Next(Methods.Length)],
                Type             = type,
                Origin           = route.Origin,
                Destination      = route.Destination,
                PassengerName    = name,
                PassengerCount   = pax,
                CardToken        = GenerateCardToken()
            };

            var payment = _paymentService.Create(request);
            Console.WriteLine(
                $"[SyntheticTraffic] Created {type} payment {payment.Id:D} " +
                $"{payment.Origin}-{payment.Destination} {payment.Amount:F2} {payment.Currency} [{payment.Method}]");
        }

        private void ResolvePayment(Guid id)
        {
            int roll = _rng.Next(100);

            // First transition: Pending → Authorised (or Declined)
            if (_paymentService.GetById(id)?.Status == PaymentStatus.Pending)
            {
                if (roll < 88)
                {
                    _paymentService.UpdateStatus(id, PaymentStatus.Authorised);
                    return; // will be captured on next status tick
                }
                else
                {
                    var decline = DeclineReasons[_rng.Next(DeclineReasons.Length)];
                    _paymentService.UpdateStatus(id, PaymentStatus.Declined, decline.Code, decline.Message);
                    Console.WriteLine($"[SyntheticTraffic] Payment {id:D} DECLINED: {decline.Message}");
                    return;
                }
            }

            // Second transition: Authorised → Captured (or Refunded occasionally)
            if (_paymentService.GetById(id)?.Status == PaymentStatus.Authorised)
            {
                if (roll < 92)
                {
                    _paymentService.UpdateStatus(id, PaymentStatus.Captured);
                }
                else if (roll < 97)
                {
                    _paymentService.UpdateStatus(id, PaymentStatus.Refunded);
                    Console.WriteLine($"[SyntheticTraffic] Payment {id:D} REFUNDED");
                }
                else
                {
                    _paymentService.UpdateStatus(id, PaymentStatus.Failed,
                        "CAPTURE_FAILED", "Capture request timed out");
                }
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────────

        private decimal CalculateAmount(PaymentType type, decimal baseAmount, int pax)
        {
            switch (type)
            {
                case PaymentType.FlightBooking:
                    decimal tax   = Math.Round(baseAmount * 0.18m, 2);
                    decimal noise = (decimal)(_rng.NextDouble() * 30.0 - 10.0);
                    return Math.Max(9.99m, Math.Round((baseAmount + tax + noise) * pax, 2));

                case PaymentType.Ancillary:
                    decimal ancBase = (decimal)(5.0 + _rng.NextDouble() * 45.0);
                    return Math.Round(ancBase * pax, 2);

                case PaymentType.Refund:
                    return -Math.Round(baseAmount * pax * (decimal)(0.5 + _rng.NextDouble() * 0.5), 2);

                case PaymentType.Amendment:
                    return Math.Round((decimal)(15.0 + _rng.NextDouble() * 35.0) * pax, 2);

                default:
                    return baseAmount;
            }
        }

        private string GenerateBookingReference()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            char[] result = new char[6];
            for (int i = 0; i < result.Length; i++)
                result[i] = chars[_rng.Next(chars.Length)];
            return $"FR-{new string(result)}";
        }

        private string GenerateCardToken()
        {
            return $"{_rng.Next(4000, 5600):D4}{_rng.Next(1000, 9999):D4}{_rng.Next(1000, 9999):D4}{_rng.Next(1000, 9999):D4}";
        }

        private int NextInterval() => _rng.Next(MinIntervalMs, MaxIntervalMs + 1);
    }
}
