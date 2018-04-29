using System;
using System.Configuration;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;

namespace SunriseSunsetClient
{
    class SunriseSunset
    {
        public DateTime Sunrise { get; set; }
        public DateTime Sunset { get; set; }
    }

    class Program
    {
        enum Light
        {
            Unknown,
            On,
            Off
        }

        static void Main(string[] args)
        {
            var light = Light.Unknown;
            SunriseSunset sunriseSunsetInfo = null;

            while (true)
            {
                try
                {
                    // check on startup or everyday at 4am
                    if (sunriseSunsetInfo == null || (sunriseSunsetInfo.Sunrise.Day < DateTime.Now.Day &&
                                                      DateTime.Now.Hour == 4 && DateTime.Now.Minute == 0))
                    {
                        // use a temp variable that way if the call fails after 3 tries but we still have 
                        // data from a previous successful call we can still use that
                        for (var i = 0; i < 3; i++)
                        {
                            var tmpSunriseSunsetInfo = GetSunriseSunsetInfo();
                            if (tmpSunriseSunsetInfo != null)
                            {
                                sunriseSunsetInfo = tmpSunriseSunsetInfo;
                                Console.WriteLine("[{0} {1}] Today's sunrise is at {2} and sunset is at {3}",
                                    DateTime.Now.ToString("M/d/yyyy"),
                                    DateTime.Now.ToString("h:mm tt"),
                                    sunriseSunsetInfo.Sunrise.ToString("h:mm tt"),
                                    sunriseSunsetInfo.Sunset.ToString("h:mm tt"));
                                break;
                            }
                            Console.WriteLine("Error trying to get sunrise/sunset data, retrying...");
                            Thread.Sleep(30000); // wait 30 seconds and try again
                        }
                    }

                    if (sunriseSunsetInfo != null)
                    {
                        var daytime = (DateTime.Now >= sunriseSunsetInfo.Sunrise &&
                                       DateTime.Now < sunriseSunsetInfo.Sunset);

                        if (daytime)
                        {
                            if (light != Light.Off)
                            {
                                // turn light off
                                Console.WriteLine("[{0} {1}] Turning light off",
                                    DateTime.Now.ToString("M/d/yyyy"),
                                    DateTime.Now.ToString("h:mm tt"));
                                light = Light.Off;
                            }
                        }
                        else
                        {
                            if (light != Light.On)
                            {
                                // turn light on
                                Console.WriteLine("[{0} {1}] Turning light on",
                                    DateTime.Now.ToString("M/d/yyyy"),
                                    DateTime.Now.ToString("h:mm tt"));
                                light = Light.On;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Failed to get sunrise/sunset data");
                    }

                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }

            }
        }

        private static SunriseSunset GetSunriseSunsetInfo()
        {
            SunriseSunset sunriseSunsetInfo = null;

            var latitude = ConfigurationManager.AppSettings["latitude"];
            var longitude = ConfigurationManager.AppSettings["longitude"];

            var client = new HttpClient { BaseAddress = new Uri("http://api.sunrise-sunset.org") };
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = client.GetAsync(string.Format("json?lat={0}&lng={1}", latitude, longitude)).Result;

            if (response.IsSuccessStatusCode)
            {
                dynamic sunriseSunset = response.Content.ReadAsAsync<dynamic>().Result.results;

                sunriseSunsetInfo = new SunriseSunset 
                {
                    Sunrise = DateTime.ParseExact(sunriseSunset.sunrise.ToString(), "hh:mm:ss tt",
                        DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal),
                    Sunset = DateTime.ParseExact(sunriseSunset.sunset.ToString(), "hh:mm:ss tt",
                        DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal)
                };
            }
            return sunriseSunsetInfo;
        }
    }
}
