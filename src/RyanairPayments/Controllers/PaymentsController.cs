using System;
using System.Net;
using System.Web.Http;
using RyanairPayments.Models;
using RyanairPayments.Services;

namespace RyanairPayments.Controllers
{
    [RoutePrefix("api/payments")]
    public class PaymentsController : ApiController
    {
        private readonly IPaymentService _payments = PaymentService.Instance;

        /// <summary>GET /api/payments?limit=50 — most recent payments, newest first</summary>
        [HttpGet, Route("")]
        public IHttpActionResult GetAll([FromUri] int limit = 50)
        {
            if (limit < 1 || limit > 500)
                return BadRequest("limit must be between 1 and 500.");

            return Ok(_payments.GetRecent(limit));
        }

        /// <summary>GET /api/payments/stats — aggregated counters and breakdowns</summary>
        [HttpGet, Route("stats")]
        public IHttpActionResult GetStats()
        {
            return Ok(_payments.GetStats());
        }

        /// <summary>GET /api/payments/{id} — single payment by GUID</summary>
        [HttpGet, Route("{id:guid}")]
        public IHttpActionResult GetById(Guid id)
        {
            var payment = _payments.GetById(id);
            return payment != null ? (IHttpActionResult)Ok(payment) : NotFound();
        }

        /// <summary>POST /api/payments — submit a new payment</summary>
        [HttpPost, Route("")]
        public IHttpActionResult Create([FromBody] PaymentRequest request)
        {
            if (request == null)
                return BadRequest("Request body is required.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var payment = _payments.Create(request);
            return Created(new Uri($"api/payments/{payment.Id}", UriKind.Relative), payment);
        }

        /// <summary>PATCH /api/payments/{id}/status — manually override a payment status (testing)</summary>
        [HttpPatch, Route("{id:guid}/status")]
        public IHttpActionResult UpdateStatus(Guid id, [FromBody] StatusUpdateRequest update)
        {
            if (update == null)
                return BadRequest("Request body is required.");

            bool updated = _payments.UpdateStatus(id, update.Status, update.ErrorCode, update.ErrorMessage);
            if (!updated)
                return NotFound();

            return Ok(_payments.GetById(id));
        }
    }

    public class StatusUpdateRequest
    {
        public PaymentStatus Status       { get; set; }
        public string        ErrorCode    { get; set; }
        public string        ErrorMessage { get; set; }
    }
}
