namespace RyanairPayments.Models
{
    public enum PaymentStatus
    {
        Pending,
        Authorised,
        Captured,
        Declined,
        Refunded,
        Cancelled,
        Failed
    }

    public enum PaymentMethod
    {
        Visa,
        Mastercard,
        Amex,
        PayPal,
        ApplePay,
        GooglePay
    }

    public enum PaymentType
    {
        FlightBooking,
        Ancillary,
        Refund,
        Amendment
    }

    public enum Currency
    {
        EUR,
        GBP,
        PLN,
        CZK
    }
}
