using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SunriseSunsetLib
{
    public class SunriseSunsetRestClient
    {
        public static async Task<SunriseSunset> GetSunriseSunsetInfo(string latitude, string longitude)
        {
            var client = new HttpClient { BaseAddress = new Uri("http://api.sunrise-sunset.org") };
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = await client.GetAsync(string.Format("json?lat={0}&lng={1}", latitude, longitude));
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsAsync<dynamic>();
            dynamic sunriseSunset = content.results;

            var sunriseSunsetInfo = new SunriseSunset
            {
                Sunrise = DateTimeOffset.ParseExact(sunriseSunset.sunrise.ToString(), "hh:mm:ss tt",
                    DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal),
                Sunset = DateTimeOffset.ParseExact(sunriseSunset.sunset.ToString(), "hh:mm:ss tt",
                    DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal)
            };

            // if sunset is > midnight UTC, the sunrise-sunset api does not properly add a day, therefore sunset
            // comes back as 12AM on the same day of the sunrise instead of the following day, so compare the
            // times and compensate if necessary.
            if (sunriseSunsetInfo.Sunset < sunriseSunsetInfo.Sunrise)
                sunriseSunsetInfo.Sunset = sunriseSunsetInfo.Sunset.AddDays(1);

            return sunriseSunsetInfo;
        }
    }
}
