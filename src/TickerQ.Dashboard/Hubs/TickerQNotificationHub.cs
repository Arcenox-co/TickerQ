using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TickerQ.Dashboard.Authentication;

namespace TickerQ.Dashboard.Hubs
{
    public class TickerQNotificationHub : Hub
    {
        private readonly IConfiguration _config;
        private readonly ILogger<TickerQNotificationHub> _logger;
        private readonly IAuthService _authService;

        public TickerQNotificationHub(
            IConfiguration config, 
            ILogger<TickerQNotificationHub> logger,
            IAuthService authService)
        {
            _config = config;
            _logger = logger;
            _authService = authService;
        }

        public override async Task OnConnectedAsync()
        {
            var connectionId = Context.ConnectionId;
            _logger.LogDebug("SignalR connection attempt: {ConnectionId}", connectionId);

            // Authenticate the connection using new auth service
            var authResult = await _authService.AuthenticateAsync(Context.GetHttpContext()!);
            
            if (!authResult.IsAuthenticated)
            {
                _logger.LogWarning("SignalR authentication failed: {ConnectionId} - {Error}", 
                    connectionId, authResult.ErrorMessage);
                Context.Abort();
                return;
            }

            _logger.LogInformation("SignalR connection established: {ConnectionId} - User: {Username}", 
                connectionId, authResult.Username);

            // Store user info in connection
            Context.Items["username"] = authResult.Username;
            Context.Items["authenticated"] = true;

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var connectionId = Context.ConnectionId;
            var username = Context.Items["username"]?.ToString() ?? "unknown";
            
            _logger.LogInformation("SignalR connection disconnected: {ConnectionId} - User: {Username}", 
                connectionId, username);

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinGroup(string groupName)
        {
            if (!IsAuthenticated())
            {
                await Clients.Caller.SendAsync("Error", "Authentication required");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            var username = Context.Items["username"]?.ToString();
            
            _logger.LogDebug("User {Username} joined group {GroupName}", username, groupName);
            await Clients.Caller.SendAsync("GroupJoined", groupName);
        }
        
        public async Task LeaveGroup(string groupName)
        {
            if (!IsAuthenticated())
            {
                await Clients.Caller.SendAsync("Error", "Authentication required");
                return;
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            var username = Context.Items["username"]?.ToString();
            
            _logger.LogDebug("User {Username} left group {GroupName}", username, groupName);
            await Clients.Caller.SendAsync("GroupLeft", groupName);
        }
        
        public async Task GetStatus()
        {
            var status = new
            {
                connectionId = Context.ConnectionId,
                authenticated = IsAuthenticated(),
                username = Context.Items["username"]?.ToString() ?? "anonymous",
                timestamp = DateTime.UtcNow
            };

            await Clients.Caller.SendAsync("Status", status);
        }

        private bool IsAuthenticated()
        {
            return Context.Items.ContainsKey("authenticated") && 
                   (bool)Context.Items["authenticated"]!;
        }
    }
}