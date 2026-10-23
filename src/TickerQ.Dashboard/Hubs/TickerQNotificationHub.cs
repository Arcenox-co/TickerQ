using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using TickerQ.Dashboard.Authentication;

namespace TickerQ.Dashboard.Hubs
{
    public class TickerQNotificationHub : Hub
    {
        private readonly ILogger<TickerQNotificationHub> _logger;
        private readonly IAuthService _authService;
        private readonly Infrastructure.Metrics.ITickerQDashboardMetrics _metrics;

        public TickerQNotificationHub(
            ILogger<TickerQNotificationHub> logger,
            IAuthService authService,
            Infrastructure.Metrics.ITickerQDashboardMetrics metrics)
        {
            _logger = logger;
            _authService = authService;
            _metrics = metrics;
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
            _metrics.SignalRConnected();

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var connectionId = Context.ConnectionId;
            var username = Context.Items["username"]?.ToString() ?? "unknown";

            _logger.LogInformation("SignalR connection disconnected: {ConnectionId} - User: {Username}",
                connectionId, username);

            // Only counted when the connection passed auth — aborted
            // connections never incremented.
            if (IsAuthenticated())
                _metrics.SignalRDisconnected();

            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinGroup(string groupName)
        {
            if (!IsAuthenticated())
            {
                await Clients.Caller.SendAsync("Error", "Authentication required");
                return;
            }

            var normalizedGroupName = NormalizeCronGroupName(groupName);
            await Groups.AddToGroupAsync(Context.ConnectionId, normalizedGroupName);
            var username = Context.Items["username"]?.ToString();
            
            _logger.LogDebug("User {Username} joined group {GroupName}", username, normalizedGroupName);
            await Clients.Caller.SendAsync("GroupJoined", normalizedGroupName);
        }
        
        public async Task LeaveGroup(string groupName)
        {
            if (!IsAuthenticated())
            {
                await Clients.Caller.SendAsync("Error", "Authentication required");
                return;
            }

            var normalizedGroupName = NormalizeCronGroupName(groupName);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, normalizedGroupName);
            var username = Context.Items["username"]?.ToString();
            
            _logger.LogDebug("User {Username} left group {GroupName}", username, normalizedGroupName);
            await Clients.Caller.SendAsync("GroupLeft", normalizedGroupName);
        }

        internal static string NormalizeCronGroupName(string? groupName)
        {
            if (!Guid.TryParse(groupName, out var cronTickerId))
                throw new HubException("Cron group must be a valid ticker ID.");

            return cronTickerId.ToString("D");
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
