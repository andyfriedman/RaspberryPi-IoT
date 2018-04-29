using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.ServiceBus.Messaging;
using SunriseSunsetLib;

namespace LightControllerWeb.Controllers
{
    enum Light
    {
        Unknown,
        On,
        Off
    }

    public class LightsController : ApiController
    {
        private readonly TopicClient _topicClientProd, _topicClientDev;

        public LightsController()
        {
            _topicClientProd = TopicClient.CreateFromConnectionString(Global.ConnectionString, Global.TopicNameProd);
            _topicClientDev = TopicClient.CreateFromConnectionString(Global.ConnectionString, Global.TopicNameDev);
        }

        public async Task<HttpResponseMessage> Get(string latitude, string longitude, int sunriseTimeOffsetMinutes = 0, int sunsetTimeOffsetMinutes = 0)
        {
            var sunriseSunsetInfo = await SunriseSunsetRestClient.GetSunriseSunsetInfo(latitude, longitude);

            bool daylight = false;
            if (sunriseSunsetInfo != null)
            {
                var adjustedSunrise = sunriseSunsetInfo.Sunrise.AddMinutes(sunriseTimeOffsetMinutes);
                var adjustedSunset = sunriseSunsetInfo.Sunset.AddMinutes(sunsetTimeOffsetMinutes);

                daylight = (DateTimeOffset.UtcNow >= adjustedSunrise && 
                            DateTimeOffset.UtcNow < adjustedSunset);
            }

            return Request.CreateResponse(HttpStatusCode.OK, new { daylight });
        }

        public async Task<HttpResponseMessage> Put(string lightSwitch, string command, bool dev = false)
        {
            var lightState = (Light)Enum.Parse(typeof(Light), Capitalize(command));
            PublishLightState(lightSwitch.ToLower(), lightState, dev);
            return Request.CreateResponse(HttpStatusCode.OK);
        }

        private void PublishLightState(string lightSwitch, Light lightState, bool publishToDev)
        {
            using (var message = new BrokeredMessage { MessageId = Guid.NewGuid().ToString() })
            {
                message.Properties["LightState"] = lightState.ToString();
                message.Properties["RecipientId"] = lightSwitch;

                if (publishToDev)
                {
                    _topicClientDev.Send(message);
                    Trace.TraceInformation("Published light state message ({0}) to topic \"{1}\", message id: {2}",
                        lightState, Global.TopicNameProd, message.MessageId);
                }
                else
                {
                    _topicClientProd.Send(message);
                    Trace.TraceInformation("Published light state message ({0}) to topic \"{1}\", message id: {2}",
                        lightState, Global.TopicNameProd, message.MessageId);
                }
            }
        }

        private static string Capitalize(string word)
        {
            if (string.IsNullOrEmpty(word))
                throw new ArgumentNullException("word");

            var wordBuffer = word.ToLower().ToCharArray();
            wordBuffer[0] = Convert.ToChar(Convert.ToString(wordBuffer[0]).ToUpper());
            return new string(wordBuffer);
        }
    }
}
