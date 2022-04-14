using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

using Azure;
using Azure.Data.Tables;

using Microsoft.Extensions.Configuration;

namespace P2pCommunicationPoc
{
    public class EndpointsInfo
    {
        public string LocalNetworkAddress = String.Empty;

        public int LocalNetworkPort = 0;

        public string ExternalNetworkAddress = String.Empty;

        public int ExternalNetworkPort = 0;

        public string WifiDirectNetworkAddress = String.Empty;

        public int WifiDirectNetworkPort = 0;

        public string RelayNetworkAddress = String.Empty;

        public int RelayNetworkPort = 0;

    }

    public class EndpointsHelper
    {

        public static EndpointsInfo GetMyConnectionEndpointsInfo(string myId)
        {
            //TODO: get local and external DNS 
            //TODO: get candidate port numbers

            //var host = Dns.GetHostEntry(Dns.GetHostName());
            //string localIpAddress = string.Empty;
            //foreach (var ip in host.AddressList)
            //{
            //    if (ip.AddressFamily == AddressFamily.InterNetwork)
            //    {
            //        localIpAddress = ip.ToString();
            //        break;
            //    }
            //}
            return null;
        }

        public static void RegisterMyConnectionEndpointsInfo(EndpointsInfo myConnectionEndpointsInfo)
        {

        }

        public static EndpointsInfo GetPeerConnectionEndpointsInfo(string peerId)
        {
            return null;
        }

        public static EndpointsInfo GetConnectionEndpointsInfoFromAppConfig(string id)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            var myEndpointsInfo = configuration.GetSection(id);
            return new EndpointsInfo
            {
                LocalNetworkAddress = myEndpointsInfo.GetValue<string>("LOCAL_NETWORK_ADDRESS"),
                LocalNetworkPort = myEndpointsInfo.GetValue<int>("LOCAL_NETWORK_PORT")
            };

        }


    }
}
