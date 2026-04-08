using System;
using System.Collections.Generic;
using RyanairPayments.Models;

namespace RyanairPayments.Services
{
    public interface IPaymentService
    {
        Payment              Create(PaymentRequest request);
        Payment              GetById(Guid id);
        IEnumerable<Payment> GetRecent(int limit = 50);
        PaymentStats         GetStats();
        bool                 UpdateStatus(Guid id, PaymentStatus status, string errorCode = null, string errorMessage = null);
        int                  TotalCount { get; }
    }
}
