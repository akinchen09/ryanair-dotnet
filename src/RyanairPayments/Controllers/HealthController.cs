using System;
using System.Reflection;
using System.Web.Http;
using RyanairPayments.Services;

namespace RyanairPayments.Controllers
{
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        private static readonly DateTime _startTime = DateTime.UtcNow;

        /// <summary>GET /api/health — liveness probe</summary>
        [HttpGet, Route("")]
        public IHttpActionResult Get()
        {
            return Ok(new
            {
                status      = "healthy",
                service     = "ryanair-payments",
                version     = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                environment = "container",
                uptime      = FormatUptime(DateTime.UtcNow - _startTime),
                timestamp   = DateTime.UtcNow,
                payments    = new
                {
                    totalStored = PaymentService.Instance.TotalCount
                }
            });
        }

        private static string FormatUptime(TimeSpan ts)
            => $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}
