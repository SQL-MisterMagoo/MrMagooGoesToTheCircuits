using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Circuits.Server
{
    public class TrackingCircuitHandler : CircuitHandler
    {
        public TrackingCircuitHandler(ILoggerFactory loggerFactory)
        {
            LoggerFactory = loggerFactory;

        }

        private static readonly ConcurrentDictionary<string, Action<string>> Notifications = new ConcurrentDictionary<string, Action<string>>();
        private static readonly ConcurrentDictionary<string, string> CircuitUsers = new ConcurrentDictionary<string, string>();
        private readonly ILoggerFactory LoggerFactory;
        private string CircuitID;
        public string GetCircuit() => CircuitID;
        public ImmutableArray<string> Users => CircuitUsers.Values.ToImmutableArray<string>();
        public async Task<string> AddUser(string user, Action<string> action = null)
        {
            if (CircuitID is object)
            {
                CircuitUsers[CircuitID] = $"{user}";
            }
            if (action is object)
            {
                Notifications[CircuitID] = action;
            }
            await NotifyUserListClients(user).ConfigureAwait(false);
            return CircuitID;
        }

        public void WatchUsers(Action<string> action)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            if (CircuitID is object)
            {
                Notifications[CircuitID] = action;
            }
        }

        private async Task NotifyUserListClients(string user)
        {
            await Task.Factory.StartNew(() =>
            {
                foreach (var notification in Notifications)
                {
                    notification.Value?.Invoke(user);
                }
            }).ConfigureAwait(false);
        }

        public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            await base.OnCircuitOpenedAsync(circuit, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            LogEvent(circuit);
        }
        public override async Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            await base.OnCircuitClosedAsync(circuit, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            CircuitUsers.TryRemove(circuit.Id, out _);
            if (Notifications.ContainsKey(circuit.Id))
            {
                Notifications.TryRemove(circuit.Id, out _);
            }
            await NotifyUserListClients("").ConfigureAwait(false);
            LogEvent(circuit);
        }
        public override async Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            await base.OnConnectionDownAsync(circuit, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            LogEvent(circuit);
        }


        public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            await base.OnConnectionUpAsync(circuit, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            LogEvent(circuit);

        }
        private void LogEvent(Circuit circuit, [CallerMemberName]string state = null)
        {
            CircuitID = circuit.Id;
            ILogger logger = LoggerFactory
                .CreateLogger(nameof(TrackingCircuitHandler));

            CircuitUsers.TryGetValue(circuit.Id, out string user);

            if (user is object)
            {
                logger.LogInformation($"Circuit {circuit.Id} {state} for {user}");
            }
            else
            {
                logger.LogInformation($"Circuit {circuit.Id} {state} with no Auth State");
            }

            logger.LogInformation($"There are {CircuitUsers.Count} connected users");
        }
    }
}
