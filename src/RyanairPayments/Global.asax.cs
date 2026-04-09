using System;
using System.Web;
using System.Web.Http;
using NewRelic.Api.Agent;
using RyanairPayments.App_Start;
using RyanairPayments.Services;

namespace RyanairPayments
{
    public class WebApiApplication : HttpApplication
    {
        private static SyntheticTrafficGenerator _trafficGenerator;

        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);

            _trafficGenerator = new SyntheticTrafficGenerator(PaymentService.Instance);
            _trafficGenerator.Start();

            Console.WriteLine("[RyanairPayments] Application started.");
        }

        protected void Application_End(object sender, EventArgs e)
        {
            _trafficGenerator?.Stop();
            _trafficGenerator?.Dispose();
            Console.WriteLine("[RyanairPayments] Application stopping.");
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            var ex = Server.GetLastError();
            if (ex == null) return;

            // Surface all unhandled ASP.NET errors in New Relic Error Analytics
            NrApi.NoticeError(ex, new System.Collections.Generic.Dictionary<string, string>
            {
                ["error.source"]   = "Application_Error",
                ["error.type"]     = ex.GetType().Name,
                ["request.path"]   = Request?.RawUrl ?? "unknown"
            });

            Console.WriteLine($"[RyanairPayments] Unhandled error: {ex}");
        }
    }
}
