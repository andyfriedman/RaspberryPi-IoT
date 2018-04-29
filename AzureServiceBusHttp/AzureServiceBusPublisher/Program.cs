using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using AzureServiceBusHttpClient;

namespace AzureServiceBusPublisher
{
    class Program
    {
        private static readonly string ServiceBusNamespace = ConfigurationManager.AppSettings["ServiceBusNamespace"];
        private static readonly string SasKeyName = ConfigurationManager.AppSettings["SasKeyName"];
        private static readonly string SasKey = ConfigurationManager.AppSettings["SasKey"];
        private static readonly string AcsIdentity = ConfigurationManager.AppSettings["AcsIdentity"];
        private static readonly string AcsKey = ConfigurationManager.AppSettings["AcsKey"];
        private static readonly string TopicName = ConfigurationManager.AppSettings["TopicName"];

        private const int RequestTimeoutSeconds = 5; 
        private const int AuthTokenExpirationMinutes = 10;

        static void Main(string[] args)
        {
            var baseAddressHttp = "https://" + ServiceBusNamespace + ".servicebus.windows.net/";
            var topicAddress = baseAddressHttp + TopicName;

            HttpClientHelper.DisableServerCertificateValidation = true;
            HttpClientHelper httpClientHelper = null;
            Timer tokenRenewalTimer = null;
            var renewalInterval = TimeSpan.FromMinutes(AuthTokenExpirationMinutes) - TimeSpan.FromSeconds(30); // give ourselves a 30 second buffer

            if (!string.IsNullOrEmpty(SasKey))
            {
                // Create service bus http client and setup SAS token auto-renewal timer
                httpClientHelper = new HttpClientHelper(ServiceBusNamespace, SasKeyName, SasKey, AuthTokenExpirationMinutes);
                tokenRenewalTimer = new Timer(x =>
                {
                    httpClientHelper.RenewSasToken(AuthTokenExpirationMinutes);
                    Console.WriteLine("SAS token renewed");
                }, null, renewalInterval, renewalInterval);
            }
            else if (!string.IsNullOrEmpty(AcsKey))
            {
                // Create service bus http client and setup ACS token auto-renewal timer
                httpClientHelper = new HttpClientHelper(ServiceBusNamespace, AcsIdentity, AcsKey);
                tokenRenewalTimer = new Timer(x =>
                {
                    httpClientHelper.RenewAcsToken();
                    Console.WriteLine("ACS token renewed");
                }, null, renewalInterval, renewalInterval);
            }

            if (httpClientHelper == null)
                throw new Exception("Failed to acquire authorization token");

            // Create topic of size 1GB. Specify a default TTL of 10 minutes. Time durations
            // are formatted according to ISO 8610 (see http://en.wikipedia.org/wiki/ISO_8601#Durations).
            Console.WriteLine("Creating topic ...");
            var topicDescription = Encoding.UTF8.GetBytes(File.ReadAllText(".\\EntityDescriptions\\TopicDescription.xml"));
            httpClientHelper.CreateEntity(topicAddress, topicDescription, RequestTimeoutSeconds).Wait();

            // Optionally query the topic.
            Console.WriteLine("Query Topic ...");
            var queryTopicResponse = httpClientHelper.GetEntity(topicAddress, RequestTimeoutSeconds).Result;
            Console.WriteLine("Topic:\n" + Encoding.UTF8.GetString(queryTopicResponse));

            // start the message publishing loop
            var i = 0;
            while (true)
            {
                // Send message to the topic.
                Console.WriteLine("Sending message {0}...", ++i);
                var message = new ServiceBusHttpMessage
                {
                    Body = Encoding.UTF8.GetBytes("This is message #" + i),
                    BrokerProperties =
                    {
                        Label = "M1", 
                        MessageId = i.ToString()
                    }
                };

                // adding custom properties effectively adds custom HTTP headers
                message.CustomProperties["Priority"] = "High";
                message.CustomProperties["CustomerId"] = "12345";
                message.CustomProperties["CustomerName"] = "ABC";
                message.CustomProperties["RecipientId"] = Dns.GetHostName().ToLower();

                httpClientHelper.SendMessage(topicAddress, message, RequestTimeoutSeconds).Wait();
                Thread.Sleep(5000);
            }
        }
    }
}
