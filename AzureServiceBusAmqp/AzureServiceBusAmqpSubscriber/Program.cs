using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amqp;

namespace AzureServiceBusAmqpSubscriber
{
    class Program
    {
        private static readonly string TopicName = ConfigurationManager.AppSettings["TopicName"];
        private static readonly string SubscriptionName = ConfigurationManager.AppSettings["SubscriptionName"];

        static void Main(string[] args)
        {
            Trace.TraceLevel = TraceLevel.Frame;
            Trace.TraceListener = (f, a) => 
                System.Diagnostics.Trace.WriteLine(DateTime.Now.ToString("[hh:ss.fff]") + " " + string.Format(f, a));

            // Ignore root certificate store and accept all SSL certificates.
            Connection.DisableServerCertValidation = true;

            var amqpConnection = new Connection(
                new Address("amqps://RootManageSharedAccessKey:WJAsH2GQrfmeFG7fGFZroFZcau8L7Y59lszO7UbnWTE=@bus001.servicebus.windows.net"))
            {
                Closed = (sender, error) => { Console.WriteLine("Connection closed"); },
            };

            var amqpSession = new Session(amqpConnection)
            {
                Closed = (sender, error) => { Console.WriteLine("Session closed"); }
            };

            var amqpReceiver = new ReceiverLink(amqpSession,
                string.Format("receive-link/{0}/{1}", TopicName, SubscriptionName), // unique name for all links from this client
                string.Format("{0}/subscriptions/{1}", TopicName, SubscriptionName)) // Service Bus topic/subscription name
            {
                Closed = (receiver, error) => { Console.WriteLine("ReceiverLink closed"); }
            };

            amqpReceiver.Start(1, (receiver, msg) =>
            {
                Console.WriteLine("Message received:\n\t" + msg.Properties.ToString() + "\n\t" + msg.ApplicationProperties.ToString());
                amqpReceiver.Accept(msg);
            });

            Console.WriteLine("Press ENTER to quit");
            Console.ReadLine();

            amqpReceiver.Close();
            amqpSession.Close();
            amqpConnection.Close();
        }
    }
}
