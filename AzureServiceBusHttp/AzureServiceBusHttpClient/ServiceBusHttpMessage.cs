using System.Collections.Specialized;

namespace AzureServiceBusHttpClient
{
    public class ServiceBusHttpMessage
    {
        public byte[] Body { get; set; }
        public string Location { get; set; }
        public BrokerProperties BrokerProperties { get; set; }
        public NameValueCollection CustomProperties { get; private set; }

        public ServiceBusHttpMessage()
        {
            BrokerProperties = new BrokerProperties();
            CustomProperties = new NameValueCollection();
        }
    }
}