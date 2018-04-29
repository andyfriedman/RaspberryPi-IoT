using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amqp;
using Amqp.Framing;

namespace AzureServiceBusAmqpPublisher
{
    class Program
    {
        private static readonly string TopicName = ConfigurationManager.AppSettings["TopicName"];

        private static void Main(string[] args)
        {
            Trace.TraceLevel = TraceLevel.Frame;
            Trace.TraceListener = (f, a) =>
                    System.Diagnostics.Trace.WriteLine(DateTime.Now.ToString("[hh:ss.fff]") + " " + string.Format(f, a));

            // Ignore root certificate store and accept all SSL certificates.
            Connection.DisableServerCertValidation = true;

            var amqpConnection = new Connection(
                new Address("amqps://RootManageSharedAccessKey:WJAsH2GQrfmeFG7fGFZroFZcau8L7Y59lszO7UbnWTE=@bus001.servicebus.windows.net"))
            {
                Closed = (sender, error) => { Console.WriteLine("Connection closed"); }
            };

            var amqpSession = new Session(amqpConnection)
            {
                Closed = (sender, error) => { Console.WriteLine("Session closed"); }
            };

            var amqpSender = new SenderLink(amqpSession,
                "send-link/" + TopicName, // unique name for all links from this client 
                TopicName) // Service Bus topic name 
            {
                Closed = (sender, error) => { Console.WriteLine("SenderLink closed"); }
            };

            for (var i = 1; i < 6; i++)
            {
                var properties = new Properties
                {
                    Subject = "Message #" + i, 
                    MessageId = Guid.NewGuid().ToString()
                };

                var appProperties = new ApplicationProperties();
                appProperties["MyProperty"] = "Hello World!";

                var message = new Message
                {
                    Properties = properties,
                    ApplicationProperties = appProperties
                };

                amqpSender.Send(message, (msg, outcome, state) =>
                {
                    Console.WriteLine("Message sent");
                }, null);
            }

            Console.WriteLine("Press ENTER to quit");
            Console.ReadLine();

            amqpSender.Close();
            amqpSession.Close();
            amqpConnection.Close();

        }
    }
}
