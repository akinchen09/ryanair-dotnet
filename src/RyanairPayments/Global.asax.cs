using System;
using System.Web;
using System.Web.Http;
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
            if (ex != null)
                Console.WriteLine($"[RyanairPayments] Unhandled error: {ex}");
        }
    }
}
