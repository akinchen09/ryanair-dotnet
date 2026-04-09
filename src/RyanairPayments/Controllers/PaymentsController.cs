// OTEL MIGRATION NOTE (imports):
// Remove: using NewRelic.Api.Agent;
// Add:    using System.Diagnostics;
//         using OpenTelemetry.Trace;
// Add a static ActivitySource at class level:
//         private static readonly ActivitySource _activitySource = new ActivitySource("RyanairPayments.Controllers");
// OTel spans are created per-method via _activitySource.StartActivity(...) rather than
// pulling the current transaction from a global agent singleton.

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

            // OTEL: Replace the four lines below with:
            //   using var activity = _activitySource.StartActivity("Payments.List", ActivityKind.Server);
            //   activity?.SetTag("query.limit",       limit);
            //   activity?.SetTag("query.totalStored", _payments.TotalCount);
            //   activity?.SetTag("endpoint.name",     "GET /api/payments");
            //
            // Custom span name: the name passed to StartActivity() IS the span name ("Payments.List").
            // You can also override it at any point before the span closes:
            //   activity.DisplayName = $"Payments.List limit={limit}";
            // This is the OTel equivalent of NrApi.GetAgent().CurrentTransaction.SetTransactionName().
            var txn = NrApi.GetAgent().CurrentTransaction;
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
            // OTEL: Replace the block below with:
            //   using var activity = _activitySource.StartActivity("Payments.Stats", ActivityKind.Server);
            //   activity?.SetTag("endpoint.name",     "GET /api/payments/stats");
            //   activity?.SetTag("query.totalStored", _payments.TotalCount);
            var txn = NrApi.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "Stats")
               .AddCustomAttribute("endpoint.name",     "GET /api/payments/stats")
               .AddCustomAttribute("query.totalStored", _payments.TotalCount);

            var stats = _payments.GetStats();

            // Attach key KPIs directly to the transaction for easy NRQL querying:
            // SELECT average(numeric(authorisationRate)) FROM Transaction WHERE transactionName = 'Payments/Stats'
            // OTEL: Replace the chained AddCustomAttribute calls below with:
            //   activity?.SetTag("stats.authorisationRate",   stats.AuthorisationRate);
            //   activity?.SetTag("stats.totalTransactions",   stats.TotalTransactions);
            //   activity?.SetTag("stats.captured",            stats.CapturedTransactions);
            //   activity?.SetTag("stats.declined",            stats.DeclinedTransactions);
            //   activity?.SetTag("stats.pending",             stats.PendingTransactions);
            //   activity?.SetTag("stats.totalAmountCaptured", (double)stats.TotalAmountCaptured);
            //   activity?.SetTag("stats.totalAmountRefunded", (double)stats.TotalAmountRefunded);
            // These tags appear in OTel as span attributes, queryable in NR via:
            //   SELECT average(numeric(stats.authorisationRate)) FROM Span WHERE name = 'Payments.Stats'
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
            // OTEL: Replace the block below with:
            //   using var activity = _activitySource.StartActivity("Payments.GetById", ActivityKind.Server);
            //   activity?.SetTag("endpoint.name", "GET /api/payments/{id}");
            //   activity?.SetTag("payment.id",    id.ToString());
            var txn = NrApi.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "GetById")
               .AddCustomAttribute("endpoint.name", "GET /api/payments/{id}")
               .AddCustomAttribute("payment.id",    id.ToString());

            var payment = _payments.GetById(id);
            if (payment == null)
            {
                // OTEL: Replace line below with:
                //   activity?.SetTag("query.result", "not_found");
                //   activity?.SetStatus(ActivityStatusCode.Error, "Payment not found");
                txn.AddCustomAttribute("query.result", "not_found");
                return NotFound();
            }

            // OTEL: Replace the chained AddCustomAttribute calls below with:
            //   activity?.SetTag("query.result",   "found");
            //   activity?.SetTag("payment.status", payment.Status.ToString());
            //   activity?.SetTag("payment.method", payment.Method.ToString());
            //   activity?.SetTag("payment.route",  payment.Route);
            //   activity?.SetTag("payment.amount", (double)payment.Amount);
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
            // OTEL: Replace the block below with:
            //   using var activity = _activitySource.StartActivity("Payments.Create", ActivityKind.Server);
            //   activity?.SetTag("endpoint.name",  "POST /api/payments");
            //   activity?.SetTag("request.source", "api-client");
            var txn = NrApi.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "Create")
               .AddCustomAttribute("endpoint.name",   "POST /api/payments")
               .AddCustomAttribute("request.source",  "api-client");

            if (request == null)
            {
                // OTEL: Replace line below with:
                //   activity?.SetTag("validation.result", "null_body");
                //   activity?.SetStatus(ActivityStatusCode.Error, "Null request body");
                txn.AddCustomAttribute("validation.result", "null_body");
                return BadRequest("Request body is required.");
            }

            if (!ModelState.IsValid)
            {
                // OTEL: Replace line below with:
                //   activity?.SetTag("validation.result", "invalid_model");
                //   activity?.SetStatus(ActivityStatusCode.Error, "Model validation failed");
                txn.AddCustomAttribute("validation.result", "invalid_model");
                return BadRequest(ModelState);
            }

            // OTEL: Replace the chained AddCustomAttribute calls below with:
            //   activity?.SetTag("payment.method",         request.Method.ToString());
            //   activity?.SetTag("payment.type",           request.Type.ToString());
            //   activity?.SetTag("payment.currency",       request.Currency);
            //   activity?.SetTag("payment.amount",         (double)request.Amount);
            //   activity?.SetTag("payment.route",          $"{request.Origin}-{request.Destination}");
            //   activity?.SetTag("payment.passengerCount", request.PassengerCount);
            //   activity?.SetTag("validation.result",      "valid");
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
            // OTEL: Replace the block below with:
            //   using var activity = _activitySource.StartActivity("Payments.UpdateStatus", ActivityKind.Server);
            //   activity?.SetTag("endpoint.name", "PATCH /api/payments/{id}/status");
            //   activity?.SetTag("payment.id",    id.ToString());
            var txn = NrApi.GetAgent().CurrentTransaction;
            txn.SetTransactionName("Payments", "UpdateStatus")
               .AddCustomAttribute("endpoint.name", "PATCH /api/payments/{id}/status")
               .AddCustomAttribute("payment.id",    id.ToString());

            if (update == null)
            {
                // OTEL: Replace line below with:
                //   activity?.SetTag("validation.result", "null_body");
                //   activity?.SetStatus(ActivityStatusCode.Error, "Null request body");
                txn.AddCustomAttribute("validation.result", "null_body");
                return BadRequest("Request body is required.");
            }

            // OTEL: Replace the chained AddCustomAttribute calls below with:
            //   activity?.SetTag("payment.newStatus", update.Status.ToString());
            //   activity?.SetTag("payment.errorCode", update.ErrorCode ?? string.Empty);
            //   activity?.SetTag("request.source",    "api-client-override");
            txn.AddCustomAttribute("payment.newStatus",   update.Status.ToString())
               .AddCustomAttribute("payment.errorCode",   update.ErrorCode ?? string.Empty)
               .AddCustomAttribute("request.source",      "api-client-override");

            bool updated = _payments.UpdateStatus(id, update.Status, update.ErrorCode, update.ErrorMessage);
            if (!updated)
            {
                // OTEL: Replace line below with:
                //   activity?.SetTag("update.result", "not_found");
                //   activity?.SetStatus(ActivityStatusCode.Error, "Payment not found");
                txn.AddCustomAttribute("update.result", "not_found");
                return NotFound();
            }

            // OTEL: Replace line below with:
            //   activity?.SetTag("update.result", "success");
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
