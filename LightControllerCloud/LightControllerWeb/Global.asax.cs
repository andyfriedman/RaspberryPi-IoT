using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Security;
using System.Web.SessionState;
using System.Web.Http;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure;

namespace LightControllerWeb
{
    public class Global : HttpApplication
    {
        public static readonly string ConnectionString = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.ConnectionString");
        public static readonly string TopicNameProd = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.TopicName");
        public static readonly string TopicNameDev = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.TopicName-Dev");

        void Application_Start(object sender, EventArgs e)
        {
            // Code that runs on application startup
            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            CreateTopicClient(ConnectionString, TopicNameProd);
            CreateTopicClient(ConnectionString, TopicNameDev);
        }

        private static TopicClient CreateTopicClient(string connectionString, string topic)
        {
            var namespaceManager = NamespaceManager.CreateFromConnectionString(connectionString);

            // get the topic or create if it doesn't already exist
            var topicDescription = !namespaceManager.TopicExists(topic) ?
                namespaceManager.CreateTopic(topic) :
                namespaceManager.GetTopic(topic);

            // set the default TTL
            topicDescription.DefaultMessageTimeToLive = TimeSpan.FromHours(2);
            namespaceManager.UpdateTopic(topicDescription);

            return TopicClient.CreateFromConnectionString(connectionString, topic);
        }
    }
}