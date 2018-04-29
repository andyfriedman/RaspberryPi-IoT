using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using SunriseSunsetLib;

namespace SunriseSunsetWorkerRole
{
    enum Light
    {
        Unknown,
        On,
        Off
    }
    
    public class WorkerRole : RoleEntryPoint
    {
        private static readonly string TopicNameProd = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.TopicName");
        private static readonly string TopicNameDev = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.TopicName-Dev");

        private readonly ManualResetEvent _completedEvent = new ManualResetEvent(false);
        private TopicClient _topicClientProd, _topicClientDev;

        public override void Run()
        {
            var latitude = CloudConfigurationManager.GetSetting("Latitude");
            var longitude = CloudConfigurationManager.GetSetting("Longitude");
            var timeZoneId = CloudConfigurationManager.GetSetting("TimeZoneName");
            var sunriseTimeOffsetMinutes = int.Parse(CloudConfigurationManager.GetSetting("SunriseTimeOffsetMinutes"));
            var sunsetTimeOffsetMinutes = int.Parse(CloudConfigurationManager.GetSetting("SunsetTimeOffsetMinutes"));

            var light = Light.Unknown;
            SunriseSunset sunriseSunsetInfo = null;

            // figure out the UTC value of 4am in the configured time zone
            var dailyCheckTimeLocal = new DateTime(DateTimeOffset.UtcNow.Year,
                DateTimeOffset.UtcNow.Month, DateTimeOffset.UtcNow.Day, 4, 0, 0);
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var dailyCheckTimeUtc = TimeZoneInfo.ConvertTimeToUtc(dailyCheckTimeLocal, timeZone);

            while (!_completedEvent.WaitOne(1000))
            {
                try
                {
                    // check on startup or everyday at 4am local time
                    if (sunriseSunsetInfo == null || ((sunriseSunsetInfo.Sunrise.Day < DateTimeOffset.UtcNow.Day ||
                                                       sunriseSunsetInfo.Sunrise.Month < DateTimeOffset.UtcNow.Month ||
                                                       sunriseSunsetInfo.Sunrise.Year < DateTimeOffset.UtcNow.Year) &&
                                                      DateTimeOffset.UtcNow.Hour == dailyCheckTimeUtc.Hour))
                    {
                        // use a temp variable that way if the call fails after 3 tries but we still have 
                        // data from a previous successful call we can still use that
                        for (var i = 0; i < 3; i++)
                        {
                            var tmpSunriseSunsetInfo = SunriseSunsetRestClient.GetSunriseSunsetInfo(latitude, longitude).Result;
                            if (tmpSunriseSunsetInfo != null)
                            {
                                sunriseSunsetInfo = tmpSunriseSunsetInfo;
                                Trace.TraceInformation(string.Format("Today's sunrise is at {0} UTC and sunset is at {1} UTC", 
                                    sunriseSunsetInfo.Sunrise.ToString("h:mm tt"), 
                                    sunriseSunsetInfo.Sunset.ToString("h:mm tt")));
                                break;
                            }
                            Trace.TraceError("Error trying to get sunrise/sunset data, retrying...");
                            Thread.Sleep(30000); // wait 30 seconds and try again
                        }
                    }

                    if (sunriseSunsetInfo == null)
                        throw new Exception("Failed to get sunrise/sunset data after 3 retries");
                   
                    var adjustedSunrise = sunriseSunsetInfo.Sunrise.AddMinutes(sunriseTimeOffsetMinutes);
                    var adjustedSunset = sunriseSunsetInfo.Sunset.AddMinutes(sunsetTimeOffsetMinutes);

                    var daytime = (DateTimeOffset.UtcNow >= adjustedSunrise &&
                                   DateTimeOffset.UtcNow < adjustedSunset);

                    if (daytime)
                    {
                        if (light != Light.Off)
                        {
                            // turn light off
                            Trace.TraceInformation("Turning light off");
                            PublishLightState(Light.Off);
                            light = Light.Off;
                        }
                    }
                    else
                    {
                        if (light != Light.On)
                        {
                            // turn light on
                            Trace.TraceInformation("Turning light on");
                            PublishLightState(Light.On);
                            light = Light.On;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Error: " + ex.Message);
                    Thread.Sleep(TimeSpan.FromMinutes(1)); // throttle down in case this is a recurring error
                }
            }
        }

        public override bool OnStart()
        {
            Trace.TraceInformation("Starting Sunrise/Sunset worker...");
            var connectionString = CloudConfigurationManager.GetSetting("Microsoft.ServiceBus.ConnectionString");
            
            _topicClientProd = CreateTopicClient(connectionString, TopicNameProd);
            _topicClientDev = CreateTopicClient(connectionString, TopicNameDev);

            return base.OnStart();
        }

        public override void OnStop()
        {
            Trace.TraceInformation("Sunrise/Sunset worker stopping...");

            // Close the topic connection
            _topicClientProd.Close();
            _topicClientDev.Close();
            _completedEvent.Set();

            base.OnStop();
        }

        private void PublishLightState(Light lightState)
        {
            // publish light state to prod
            using (var message = new BrokeredMessage() { MessageId = Guid.NewGuid().ToString() })
            {
                message.Properties["LightState"] = lightState.ToString();
                message.Properties["RecipientId"] = "*";
                _topicClientProd.Send(message);
                Trace.TraceInformation(string.Format("Published light state message ({0}) to topic \"{1}\", message id: {2}",
                    lightState, TopicNameProd, message.MessageId));
            }

            // publish light state to dev
            using (var message = new BrokeredMessage() { MessageId = Guid.NewGuid().ToString() })
            {
                message.Properties["LightState"] = lightState.ToString();
                message.Properties["RecipientId"] = "*";
                _topicClientDev.Send(message);
                Trace.TraceInformation(string.Format("Published light state message ({0}) to topic \"{1}\", message id: {2}",
                    lightState, TopicNameDev, message.MessageId));
            }
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
