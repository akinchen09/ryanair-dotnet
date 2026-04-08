using System.ComponentModel.DataAnnotations;

namespace RyanairPayments.Models
{
    public class PaymentRequest
    {
        [Required]
        [StringLength(20, MinimumLength = 6)]
        public string        BookingReference { get; set; }

        [Required]
        [Range(0.01, 99999.99)]
        public decimal       Amount          { get; set; }

        [Required]
        public string        Currency        { get; set; } = "EUR";

        [Required]
        public PaymentMethod Method          { get; set; }

        [Required]
        public PaymentType   Type            { get; set; }

        [Required]
        [StringLength(3, MinimumLength = 3)]
        public string        Origin          { get; set; }

        [Required]
        [StringLength(3, MinimumLength = 3)]
        public string        Destination     { get; set; }

        [Required]
        public string        PassengerName   { get; set; }

        [Range(1, 9)]
        public int           PassengerCount  { get; set; } = 1;

        public string        CardToken       { get; set; }
    }
}
