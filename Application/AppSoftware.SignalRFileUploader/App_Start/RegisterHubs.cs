using System.Web;
using System.Web.Routing;
using Microsoft.AspNet.SignalR;

[assembly: WebActivator.PreApplicationStartMethod(typeof(AppSoftware.SignalRFileUploader.App_Start.RegisterHubs), "Start")]

namespace AppSoftware.SignalRFileUploader.App_Start
{
    public static class RegisterHubs
    {
        public static void Start()
        {
            // Register the default hubs route: ~/signalr/hubs

            RouteTable.Routes.MapHubs();
        }
    }
}