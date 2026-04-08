using System;

namespace RyanairPayments.Models
{
    public class Payment
    {
        public Guid            Id                   { get; set; }
        public string          BookingReference     { get; set; }
        public decimal         Amount               { get; set; }
        public string          Currency             { get; set; }
        public PaymentMethod   Method               { get; set; }
        public PaymentStatus   Status               { get; set; }
        public PaymentType     Type                 { get; set; }
        public string          Origin               { get; set; }
        public string          Destination          { get; set; }
        public string          PassengerName        { get; set; }
        public int             PassengerCount       { get; set; }
        public DateTime        CreatedAt            { get; set; }
        public DateTime?       ProcessedAt          { get; set; }
        public string          ErrorCode            { get; set; }
        public string          ErrorMessage         { get; set; }
        public string          CardMasked           { get; set; }
        public string          ProcessorReference   { get; set; }
        public string          MerchantId           { get; set; }
        public string          AcquirerBank         { get; set; }
        public decimal?        RefundedAmount       { get; set; }
        public Guid?           OriginalPaymentId    { get; set; }

        public string Route => $"{Origin}-{Destination}";
    }
}
