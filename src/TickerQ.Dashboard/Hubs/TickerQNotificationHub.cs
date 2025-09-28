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

        public async Task JoinGroup(string groupName)
            => await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        public async Task LeaveGroup(string groupName)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }
}