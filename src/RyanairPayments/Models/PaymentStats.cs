using System;
using System.Collections.Generic;

namespace RyanairPayments.Models
{
    public class PaymentStats
    {
        public int                      TotalTransactions       { get; set; }
        public int                      CapturedTransactions    { get; set; }
        public int                      DeclinedTransactions    { get; set; }
        public int                      PendingTransactions     { get; set; }
        public int                      RefundedTransactions    { get; set; }
        public decimal                  TotalAmountCaptured     { get; set; }
        public decimal                  TotalAmountRefunded     { get; set; }
        public double                   AuthorisationRate       { get; set; }
        public Dictionary<string, int>  ByMethod                { get; set; }
        public Dictionary<string, int>  ByType                  { get; set; }
        public Dictionary<string, int>  ByStatus                { get; set; }
        public Dictionary<string, int>  ByRoute                 { get; set; }
        public DateTime                 GeneratedAt             { get; set; }
    }
}
