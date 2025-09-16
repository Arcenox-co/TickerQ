using System;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TickerQ.Dashboard.Controllers.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BasicAuthAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var builder = context.HttpContext.RequestServices.GetRequiredService<DashboardOptionsBuilder>();

            if (!builder.EnableBasicAuth)
                return;
            
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var validUser = config["TickerQBasicAuth:Username"];
            var validPass = config["TickerQBasicAuth:Password"];

            var header = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();

            if (header == null || !header.StartsWith("Basic "))
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var encodedCredentials = header["Basic ".Length..].Trim();

            // This will throw if the base64 is invalid
            var decodedBytes = Convert.FromBase64String(encodedCredentials);
            var decoded = Encoding.UTF8.GetString(decodedBytes);

            var parts = decoded.Split(':');

            if (parts.Length != 2 || parts[0] != validUser || parts[1] != validPass)
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}