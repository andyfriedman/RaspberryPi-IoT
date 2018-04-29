using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Threading;

using AzureServiceBusHttpClient;
using Raspberry.IO.GeneralPurpose;
using RaspberryPi.Logger;
using RaspberryPi.Utilities;

namespace LightNotificationClient
{
    enum Light
    {
        Unknown,
        On,
        Off
    }

    class Program
    {
        private const int AuthTokenExpirationMinutes = 60;
        private static readonly TimeSpan RenewalInterval = 
            TimeSpan.FromMinutes(AuthTokenExpirationMinutes) - 
            TimeSpan.FromSeconds(30); // give ourselves a 30 second buffer before token expires

        private static readonly bool RelayActiveLow = bool.Parse(ConfigurationManager.AppSettings["RelayActiveLow"]);
        private static OutputPinConfiguration _pin11;
        private static GpioConnection _gpio;

        private static readonly ILogger Logger = new FileLogger
        {
            LogDirectory = "logs",
            ConsoleOutput = true,
            CreateNewLogFileDaily = true
        };
        
        static void Main(string[] args)
        {
            Timer tokenRenewalTimer = null;
            var signalEvent = new ManualResetEvent(false);
            var signalHandler = new UnixSignalHandler { Logger = Logger };

            try
            {
                Logger.Info("LightNotificationClient starting...");

                // intercept Unix signals (ie. CTRL-C) so we can shut down gracefully
                signalHandler.HandleSignal(signal =>
                {
                    Logger.Info("Signal received: " + signal);
                    signalEvent.Set();
                });

                // connect to pin 11
                InitBoardGpio();

                // get the initial daylight state and turn the light on or off accordingly
                Logger.Info("Getting initial light state...");
                RetryUntilSuccess(() =>
                {
                    var lightState = (IsDaylight() ? Light.Off : Light.On);
                    Logger.Info("Setting light to " + lightState.ToString().ToLower());
                    ToggleLightSwitch(lightState);
                }, numRetries: 10, delayBetweenRetriesMs: 5000);

                // get service bus settings
                var serviceBusNamespace = ConfigurationManager.AppSettings["ServiceBusNamespace"];
                var sasKeyName = ConfigurationManager.AppSettings["SasKeyName"];
                var sasKey = ConfigurationManager.AppSettings["SasKey"];
                var topicName = ConfigurationManager.AppSettings["TopicName"];
                var subscriptionName = Dns.GetHostName();

                var topicAddress = string.Format("https://{0}.servicebus.windows.net/{1}", serviceBusNamespace, topicName);
                var subscriptionAddress = string.Format("{0}/Subscriptions/{1}", topicAddress, subscriptionName);

                // create service bus http client with SAS credentials good for 60 minutes
                HttpClientHelper.DisableServerCertificateValidation = true;
                var httpClientHelper = new HttpClientHelper(
                    serviceBusNamespace, sasKeyName, sasKey, AuthTokenExpirationMinutes) { Logger = Logger };

                // setup SAS token auto-renewal timer
                tokenRenewalTimer = new Timer(x =>
                {
                    httpClientHelper.RenewSasToken(AuthTokenExpirationMinutes);
                    Logger.Info("SAS token renewed");
                }, null, RenewalInterval, RenewalInterval);
                
                // verify the topic exists
                Logger.Info("Querying service bus topic \"{0}\"...", topicName);
                RetryUntilSuccess(() =>
                {
                    var queryTopicResponse = httpClientHelper.GetEntity(topicAddress).Result;
                    var content = Encoding.UTF8.GetString(queryTopicResponse);
                    if (!content.Contains("TopicDescription"))
                        throw new Exception(string.Format("Service bus topic \"{0}\" does not exist.", topicName));
                }, (exception) =>
                    {   // anonymous function to handle 401 unauthorized error, manually kick off the SAS renewal
                        if (exception is UnauthorizedAccessException)
                            HandleUnauthorizedAccessException(httpClientHelper, tokenRenewalTimer);
                    });
                
                // get subscription configuration
                var subscriptionDescriptionPath = Path.Combine(Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location), "SubscriptionDescription.xml");
                var subscriptionDescription = Encoding.UTF8.GetBytes(File.ReadAllText(subscriptionDescriptionPath));

                // create subscription
                Logger.Info("Registering subscription \"{0}\"...", subscriptionName);
                RetryUntilSuccess(() => httpClientHelper.CreateEntity(subscriptionAddress, subscriptionDescription).Wait(), 
                    (exception) =>
                    {   // anonymous function to handle 401 unauthorized error, manually kick off the SAS renewal
                        if (exception is UnauthorizedAccessException)
                            HandleUnauthorizedAccessException(httpClientHelper, tokenRenewalTimer);
                    });

                // delete current subscription rule
                Logger.Info("Deleting current subscription rule...");
                RetryUntilSuccess(() => httpClientHelper.DeleteEntity(subscriptionAddress + "/Rules/RecipientFilter").Wait(), 
                    (exception) =>
                    {   // anonymous function to handle 401 unauthorized error, manually kick off the SAS renewal
                        if (exception is UnauthorizedAccessException)
                            HandleUnauthorizedAccessException(httpClientHelper, tokenRenewalTimer);
                    });

                // create rule configuration
                var ruleDescriptionPath = Path.Combine(Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location), "RuleDescription.xml");
                var filterQuery = string.Format("RecipientId = '*' OR RecipientId = '{0}'", Dns.GetHostName().ToLower());
                var ruleDescription = Encoding.UTF8.GetBytes(string.Format(File.ReadAllText(ruleDescriptionPath), filterQuery));

                // create subscription rule
                Logger.Info("Creating subscription rule...");
                RetryUntilSuccess(() => httpClientHelper.CreateEntity(subscriptionAddress + "/Rules/RecipientFilter", ruleDescription).Wait(), 
                    (exception) =>
                    {   // anonymous function to handle 401 unauthorized error, manually kick off the SAS renewal
                        if (exception is UnauthorizedAccessException)
                            HandleUnauthorizedAccessException(httpClientHelper, tokenRenewalTimer);
                    });

                // now delete default subscription rule
                Logger.Info("Deleting default subscription rule...");
                RetryUntilSuccess(() => httpClientHelper.DeleteEntity(subscriptionAddress + "/Rules/$Default").Wait(), 
                    (exception) =>
                    {   // anonymous function to handle 401 unauthorized error, manually kick off the SAS renewal
                        if (exception is UnauthorizedAccessException)
                            HandleUnauthorizedAccessException(httpClientHelper, tokenRenewalTimer);
                    });
                
                var consecutiveErrors = 0;
                Exception lastError = null;
                Logger.Info("Waiting for messages...");

                // start the message receiver loop
                while (!signalEvent.WaitOne(TimeSpan.Zero))
                {
                    try
                    {
#if DEBUG
                        const int timeoutInSeconds = 5;
#else
                        const int timeoutInSeconds = 60;
#endif
                        // Receive and delete message from the subscription.
                        var message = httpClientHelper.ReceiveAndDeleteMessage(subscriptionAddress, timeoutInSeconds).Result;
                        if (message != null)
                        {
                            Logger.Info("Received message: " + message.BrokerProperties.MessageId);
                            var light = (Light)Enum.Parse(typeof (Light), message.CustomProperties["LightState"]);
                            Logger.Info("Turning light " + light.ToString().ToLower());
                            ToggleLightSwitch(light);
                        }
                        consecutiveErrors = 0;
                    }
                    catch (AggregateException ae)
                    {
                        foreach (var ex in ae.InnerExceptions)
                        {
                            if (ex is UnauthorizedAccessException)
                                HandleUnauthorizedAccessException(httpClientHelper, tokenRenewalTimer);
                            else
                                Logger.Error(ex.Message);
                            
                            lastError = ex;
                        }

                        Thread.Sleep(1000);
                        consecutiveErrors++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex.Message);
                        lastError = ex;
                        Thread.Sleep(1000);
                        consecutiveErrors++;
                    }

                    if (consecutiveErrors >= 100)
                        throw new Exception("Unrecoverable error, giving up", lastError); // something REALLY wrong, abort mission

                    if (consecutiveErrors >= 10)
                        Thread.Sleep(TimeSpan.FromMinutes(1)); // something wrong here (network issues?), let's throttle down
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal error: " + ex.Message);
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                    errorMessage += string.Format(": {0}", ex.InnerException.Message);

                SendErrorMessage(errorMessage);
            }
            finally
            {
                if (tokenRenewalTimer != null)
                    tokenRenewalTimer.Dispose();

                if (_gpio != null)
                    _gpio.Close();

                signalHandler.Dispose();
                Logger.Info("Exiting program.");
            }
        }
        
        private static void ToggleLightSwitch(Light light)
        {
            if (_gpio != null && _pin11 != null)
            {
                var pinState = _gpio.Pins[ConnectorPin.P1Pin11].Enabled;
                var pinOn = RelayActiveLow ? !pinState : pinState; // some relays are ON with low voltage signal - if so negate the state so the logic works the same
                
                if ((light == Light.On && !pinOn) ||
                    (light == Light.Off && pinOn))
                    _gpio.Toggle(_pin11);
            }
        }

        private static void InitBoardGpio()
        {
            try
            {
                _pin11 = ConnectorPin.P1Pin11.Output();
                _gpio = new GpioConnection(_pin11);
            }
            catch (Exception ex)
            {
                Logger.Error("Error initializing GPIO: " + ex.Message);
                if (ex.InnerException != null)
                    Logger.Error(ex.InnerException.Message);
            }
        }

        private static bool IsDaylight()
        {
            var baseAddress = new Uri(ConfigurationManager.AppSettings["LightControllerBaseUrl"]);
            var latitude = ConfigurationManager.AppSettings["Latitude"];
            var longitude = ConfigurationManager.AppSettings["Longitude"];
            var sunriseTimeOffsetMinutes = int.Parse(ConfigurationManager.AppSettings["SunriseTimeOffsetMinutes"]);
            var sunsetTimeOffsetMinutes = int.Parse(ConfigurationManager.AppSettings["SunsetTimeOffsetMinutes"]);

            var client = new HttpClient { BaseAddress = baseAddress };
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = client.GetAsync(string.Format(
                "api/lights?latitude={0}&longitude={1}&sunriseTimeOffsetMinutes={2}&sunsetTimeOffsetMinutes={3}", 
                latitude, longitude, sunriseTimeOffsetMinutes, sunsetTimeOffsetMinutes)).Result;
            response.EnsureSuccessStatusCode();

            dynamic result = response.Content.ReadAsAsync<dynamic>().Result;
            return result.daylight;
        }

        private static void RetryUntilSuccess(Action func, Action<Exception> errorHandler = null, int numRetries = 3, int delayBetweenRetriesMs = 1000)
        {
            Exception lastError = null;
            for (var i = 0; i < numRetries; i++)
            {
                try
                {
                    func();
                    return;
                }
                catch (AggregateException ae)
                {
                    foreach (var ex in ae.InnerExceptions)
                    {
                        if (ex is UnauthorizedAccessException)
                            Logger.Error("RetryUntilSucceeds unauthorized access error");
                        else
                            Logger.Error("RetryUntilSucceeds error: " + ex.Message);

                        if (errorHandler != null)
                            errorHandler(ex);
                        lastError = ex;
                    }
                    Thread.Sleep(delayBetweenRetriesMs);
                }
                catch (Exception ex)
                {
                    Logger.Error("RetryUntilSucceeds error: " + ex.Message);
                    if (errorHandler != null)
                        errorHandler(ex);
                    lastError = ex;
                    Thread.Sleep(delayBetweenRetriesMs);
                }
            }
            
            if (lastError != null)
                throw lastError;
        }

        private static void HandleUnauthorizedAccessException(HttpClientHelper httpClientHelper, Timer tokenRenewalTimer)
        {
            if (httpClientHelper != null && tokenRenewalTimer != null)
            {
                httpClientHelper.RenewSasToken(AuthTokenExpirationMinutes);
                tokenRenewalTimer.Change(RenewalInterval, RenewalInterval);
                Logger.Warning("SAS token renewed due to UnauthorizedAccessException");
            }
        }

        private static void SendDistressSignal()
        {
            // run this pattern of on/offs to signal that the ship is sinking
            for (var i = 0; i < 3; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    ToggleLightSwitch(Light.On);
                    Thread.Sleep(250);
                    ToggleLightSwitch(Light.Off);
                    Thread.Sleep(250);
                }
            }
            Thread.Sleep(1000);
        }

        private static void SendErrorMessage(string message)
        {
            try
            {
                var smtpHost = ConfigurationManager.AppSettings["SmtpHost"];
                var smtpUserName = ConfigurationManager.AppSettings["SmtpUserName"];
                var smtpPassword = ConfigurationManager.AppSettings["SmtpPassword"];
                var senderAddress = ConfigurationManager.AppSettings["SenderAddress"];
                var recipientAddress = ConfigurationManager.AppSettings["RecipientAddress"];

                var mail = new MailMessage
                {
                    From = new MailAddress(senderAddress),
                    Body = string.Format("{0} Failure: {1}", Dns.GetHostName(), message)
                };
                mail.To.Add(new MailAddress(recipientAddress));

                var smtp = new SmtpClient(smtpHost)
                {
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Credentials = new NetworkCredential(smtpUserName, smtpPassword)
                };
                smtp.Send(mail);
            }
            catch (Exception ex)
            {
                Logger.Error("Error sending error message: " + ex.Message);
#if DEBUG
                SendDistressSignal();
#endif
            }
        }
    }
}
