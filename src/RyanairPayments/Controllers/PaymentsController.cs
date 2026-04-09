using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using NewRelic.Api.Agent;
using RyanairPayments.Models;
using RyanairPayments.Services;

namespace RyanairPayments.Controllers
{
    [RoutePrefix("api/payments")]
    public class PaymentsController : ApiController
    {
        private readonly IPaymentService _payments = PaymentService.Instance;

        /// <summary>GET /api/payments?limit=50</summary>
        [HttpGet, Route("")]
        public IHttpActionResult GetAll([FromUri] int limit = 50)
        {
            if (limit < 1 || limit > 500)
                return BadRequest("limit must be between 1 and 500.");

            var txn = NewRelic.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "List")
               .AddCustomAttribute("query.limit",          limit)
               .AddCustomAttribute("query.totalStored",    _payments.TotalCount)
               .AddCustomAttribute("endpoint.name",        "GET /api/payments");

            var results = _payments.GetRecent(limit);
            return Ok(results);
        }

        /// <summary>GET /api/payments/stats</summary>
        [HttpGet, Route("stats")]
        public IHttpActionResult GetStats()
        {
            var txn = NewRelic.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "Stats")
               .AddCustomAttribute("endpoint.name",     "GET /api/payments/stats")
               .AddCustomAttribute("query.totalStored", _payments.TotalCount);

            var stats = _payments.GetStats();

            // Attach key KPIs directly to the transaction for easy NRQL querying:
            // SELECT average(numeric(authorisationRate)) FROM Transaction WHERE transactionName = 'Payments/Stats'
            txn.AddCustomAttribute("stats.authorisationRate",    stats.AuthorisationRate)
               .AddCustomAttribute("stats.totalTransactions",    stats.TotalTransactions)
               .AddCustomAttribute("stats.captured",             stats.CapturedTransactions)
               .AddCustomAttribute("stats.declined",             stats.DeclinedTransactions)
               .AddCustomAttribute("stats.pending",              stats.PendingTransactions)
               .AddCustomAttribute("stats.totalAmountCaptured",  (double)stats.TotalAmountCaptured)
               .AddCustomAttribute("stats.totalAmountRefunded",  (double)stats.TotalAmountRefunded);

            return Ok(stats);
        }

        /// <summary>GET /api/payments/{id}</summary>
        [HttpGet, Route("{id:guid}")]
        public IHttpActionResult GetById(Guid id)
        {
            var txn = NewRelic.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "GetById")
               .AddCustomAttribute("endpoint.name", "GET /api/payments/{id}")
               .AddCustomAttribute("payment.id",    id.ToString());

            var payment = _payments.GetById(id);
            if (payment == null)
            {
                txn.AddCustomAttribute("query.result", "not_found");
                return NotFound();
            }

            txn.AddCustomAttribute("query.result",   "found")
               .AddCustomAttribute("payment.status", payment.Status.ToString())
               .AddCustomAttribute("payment.method", payment.Method.ToString())
               .AddCustomAttribute("payment.route",  payment.Route)
               .AddCustomAttribute("payment.amount", (double)payment.Amount);

            return Ok(payment);
        }

        /// <summary>POST /api/payments — manual payment submission</summary>
        [HttpPost, Route("")]
        public IHttpActionResult Create([FromBody] PaymentRequest request)
        {
            var txn = NewRelic.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "Create")
               .AddCustomAttribute("endpoint.name",   "POST /api/payments")
               .AddCustomAttribute("request.source",  "api-client");

            if (request == null)
            {
                txn.AddCustomAttribute("validation.result", "null_body");
                return BadRequest("Request body is required.");
            }

            if (!ModelState.IsValid)
            {
                txn.AddCustomAttribute("validation.result", "invalid_model");
                return BadRequest(ModelState);
            }

            txn.AddCustomAttribute("payment.method",         request.Method.ToString())
               .AddCustomAttribute("payment.type",           request.Type.ToString())
               .AddCustomAttribute("payment.currency",       request.Currency)
               .AddCustomAttribute("payment.amount",         (double)request.Amount)
               .AddCustomAttribute("payment.route",          $"{request.Origin}-{request.Destination}")
               .AddCustomAttribute("payment.passengerCount", request.PassengerCount)
               .AddCustomAttribute("validation.result",      "valid");

            var payment = _payments.Create(request);
            return Created(new Uri($"api/payments/{payment.Id}", UriKind.Relative), payment);
        }

        /// <summary>PATCH /api/payments/{id}/status — force status for testing</summary>
        [HttpPatch, Route("{id:guid}/status")]
        public IHttpActionResult UpdateStatus(Guid id, [FromBody] StatusUpdateRequest update)
        {
            var txn = NewRelic.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "UpdateStatus")
               .AddCustomAttribute("endpoint.name", "PATCH /api/payments/{id}/status")
               .AddCustomAttribute("payment.id",    id.ToString());

            if (update == null)
            {
                txn.AddCustomAttribute("validation.result", "null_body");
                return BadRequest("Request body is required.");
            }

            txn.AddCustomAttribute("payment.newStatus",   update.Status.ToString())
               .AddCustomAttribute("payment.errorCode",   update.ErrorCode ?? string.Empty)
               .AddCustomAttribute("request.source",      "api-client-override");

            bool updated = _payments.UpdateStatus(id, update.Status, update.ErrorCode, update.ErrorMessage);
            if (!updated)
            {
                txn.AddCustomAttribute("update.result", "not_found");
                return NotFound();
            }

            txn.AddCustomAttribute("update.result", "success");
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
