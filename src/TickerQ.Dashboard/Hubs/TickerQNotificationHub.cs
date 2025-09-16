using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TickerQ.Dashboard.Hubs
{
    public class TickerQNotificationHub : Hub
    {
        private readonly IConfiguration _config;

        public TickerQNotificationHub(IConfiguration config)
        {
            _config = config;
        }

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            
            var builder = httpContext?.RequestServices.GetService<DashboardOptionsBuilder>();

            if (builder is { EnableBasicAuth: false })
            {
                await base.OnConnectedAsync();
                return;
            }
            
            var authParam = httpContext?.Request.Query["auth"].FirstOrDefault();
            
            if (string.IsNullOrWhiteSpace(authParam))
            {
                Context.Abort();
                return;
            }

            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authParam));
                var parts = decoded.Split(':');
                var username = parts[0];
                var password = parts[1];

                var validUser = _config["TickerQBasicAuth:Username"];
                var validPass = _config["TickerQBasicAuth:Password"];

                if (username != validUser || password != validPass)
                {
                    Context.Abort();
                }
            }
            catch
            {
                Context.Abort();
            }

            await base.OnConnectedAsync();
        }
        public async Task JoinGroup(string groupName)
            => await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        public async Task LeaveGroup(string groupName)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }
}