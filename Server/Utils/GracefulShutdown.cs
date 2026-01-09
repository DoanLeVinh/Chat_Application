using System;
using Microsoft.Extensions.Hosting;

namespace ChatServer.Utils
{
    public class GracefulShutdown : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("[Shutdown] Ready to handle shutdown");
            return Task.CompletedTask;
        }
        
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("[Shutdown] Server is shutting down...");
            
            // Thông báo cho tất cả client
            BroadcastShutdownMessage();
            
            Console.WriteLine("[Shutdown] Goodbye!");
            return Task.CompletedTask;
        }
        
        private void BroadcastShutdownMessage()
        {
            Console.WriteLine("[Shutdown] Broadcasting shutdown message to all clients");
            // TODO: Gửi message đến tất cả WebSocket connections
        }
    }
}