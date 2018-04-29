using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using AzureServiceBusHttpClient;

namespace AzureServiceBusSubscriber
{
    class Program
    {
        private static readonly string ServiceBusNamespace = ConfigurationManager.AppSettings["ServiceBusNamespace"];
        private static readonly string SasKeyName = ConfigurationManager.AppSettings["SasKeyName"];
        private static readonly string SasKey = ConfigurationManager.AppSettings["SasKey"];
        private static readonly string AcsIdentity = ConfigurationManager.AppSettings["AcsIdentity"];
        private static readonly string AcsKey = ConfigurationManager.AppSettings["AcsKey"];
        private static readonly string TopicName = ConfigurationManager.AppSettings["TopicName"];
        private static readonly string SubscriptionName = ConfigurationManager.AppSettings["SubscriptionName"];

        private const int RequestTimeoutSeconds = 5;
        private const int AuthTokenExpirationMinutes = 10;

        static void Main(string[] args)
        {
            var baseAddressHttp = "https://" + ServiceBusNamespace + ".servicebus.windows.net/";
            var topicAddress = baseAddressHttp + TopicName;
            var subscriptionAddress = topicAddress + "/Subscriptions/" + SubscriptionName;

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
            Console.WriteLine("Creating topic...");
            var topicDescription = Encoding.UTF8.GetBytes(File.ReadAllText(".\\EntityDescriptions\\TopicDescription.xml"));
            httpClientHelper.CreateEntity(topicAddress, topicDescription, RequestTimeoutSeconds).Wait();

            // Optionally query the topic.
            Console.WriteLine("Query Topic...");
            var queryTopicResponse = httpClientHelper.GetEntity(topicAddress, RequestTimeoutSeconds).Result;
            Console.WriteLine("Topic:\n" + Encoding.UTF8.GetString(queryTopicResponse));

            // Create subscription with default settings.
            Console.WriteLine("Creating subscription...");
            var subscriptionDescription = Encoding.UTF8.GetBytes(File.ReadAllText(".\\EntityDescriptions\\SubscriptionDescription.xml"));
            httpClientHelper.CreateEntity(subscriptionAddress, subscriptionDescription, RequestTimeoutSeconds).Wait();

            // Delete current subscription rule
            Console.WriteLine("Deleting current subscription rule...");
            httpClientHelper.DeleteEntity(subscriptionAddress + "/Rules/RecipientFilter", RequestTimeoutSeconds).Wait();

            // Create subscription rule
            Console.WriteLine("Create subscription rule...");
            var filterQuery = string.Format("RecipientId = '*' OR RecipientId = '{0}'", Dns.GetHostName().ToLower());
            var ruleDescription = Encoding.UTF8.GetBytes(string.Format(File.ReadAllText(".\\EntityDescriptions\\RuleDescription.xml"), filterQuery));
            httpClientHelper.CreateEntity(subscriptionAddress + "/Rules/RecipientFilter", ruleDescription, RequestTimeoutSeconds).Wait();

            // Delete the default rule
            Console.WriteLine("Deleting default rule...");
            httpClientHelper.DeleteEntity(subscriptionAddress + "/Rules/$Default", RequestTimeoutSeconds).Wait();

            // Optionally query the subscription rules.
            Console.WriteLine("Query subscription sules...");
            var querySubscriptionRulesResponse = httpClientHelper.GetEntity(subscriptionAddress + "/rules", RequestTimeoutSeconds).Result;
            Console.WriteLine("Subscription Rules:\n" + Encoding.UTF8.GetString(querySubscriptionRulesResponse));

            // start the message receiver loop
            Console.WriteLine("Waiting for messages...");
            while (true)
            {
                try
                {
                    // Receive and delete message from the subscription.
                    var message = httpClientHelper.ReceiveAndDeleteMessage(subscriptionAddress, RequestTimeoutSeconds).Result;
                    if (message != null)
                    {
                        Console.WriteLine("Receiving message {0}...", message.BrokerProperties.MessageId);
                        ProcessMessage(message);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
            }
        }

        private static void ProcessMessage(ServiceBusHttpMessage message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            if (message != null)
            {
                Console.WriteLine("Body           : " + Encoding.UTF8.GetString(message.Body));
                Console.WriteLine("Message ID     : " + message.BrokerProperties.MessageId);
                Console.WriteLine("Label          : " + message.BrokerProperties.Label);
                Console.WriteLine("SequenceNumber : " + message.BrokerProperties.SequenceNumber);
                Console.WriteLine("TTL            : " + message.BrokerProperties.TimeToLive + " seconds");
                Console.WriteLine("EnqueuedTime   : " + message.BrokerProperties.EnqueuedTimeUtcDateTime + " UTC");
                Console.WriteLine("Locked until   : " + (message.BrokerProperties.LockedUntilUtcDateTime == null ? 
                    "unlocked" : message.BrokerProperties.LockedUntilUtcDateTime + " UTC"));
                
                foreach (var key in message.CustomProperties.AllKeys)
                {
                    Console.WriteLine("Custom property: " + key + " = " + message.CustomProperties[key]);
                }
            }
            else
            {
                Console.WriteLine("(No message)");
            }
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }
}
