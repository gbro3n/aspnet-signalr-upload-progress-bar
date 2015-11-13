using System.Threading;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;

namespace AppSoftware.SignalRFileUploader.Hubs
{
    [HubName("uploadHub")]
    public class UploadHub : Hub
    {
        public void UploadHubTest()
        {
            Clients.Caller.updateProgress(10);
            Thread.Sleep(500);

            Clients.Caller.updateProgress(40);
            Thread.Sleep(500);

            Clients.Caller.updateProgress(64);
            Thread.Sleep(500);

            Clients.Caller.updateProgress(77);
            Thread.Sleep(500);

            Clients.Caller.updateProgress(92);
            Thread.Sleep(500);

            Clients.Caller.updateProgress(99);
            Thread.Sleep(500);

            Clients.Caller.updateProgress(100);   
        }

        public void NotifyClientPercentComplete(decimal percentComplete, string clientId)
        {
            Clients.Client(clientId).updateProgress(percentComplete);
        }
    }
}
