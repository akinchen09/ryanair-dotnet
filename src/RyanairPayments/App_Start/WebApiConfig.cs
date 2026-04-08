using System.Web.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace RyanairPayments.App_Start
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Attribute routing for [Route] / [RoutePrefix] on controllers
            config.MapHttpAttributeRoutes();

            // Convention-based fallback
            config.Routes.MapHttpRoute(
                name:         "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults:     new { id = RouteParameter.Optional }
            );

            // Return JSON by default; remove XML formatter
            var json = config.Formatters.JsonFormatter;
            json.SerializerSettings.ContractResolver        = new CamelCasePropertyNamesContractResolver();
            json.SerializerSettings.Converters.Add(new StringEnumConverter());
            json.SerializerSettings.NullValueHandling       = NullValueHandling.Ignore;
            json.SerializerSettings.DateTimeZoneHandling    = DateTimeZoneHandling.Utc;
            json.SerializerSettings.DateFormatHandling      = DateFormatHandling.IsoDateFormat;
            json.SerializerSettings.Formatting              = Formatting.None;

            config.Formatters.Remove(config.Formatters.XmlFormatter);
        }
    }
}
