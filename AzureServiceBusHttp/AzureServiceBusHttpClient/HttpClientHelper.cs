using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using RaspberryPi.Logger;

namespace AzureServiceBusHttpClient
{
    public class HttpClientHelper
    {
        private const string ApiVersion = "&api-version=2014-01"; // API version 2013-03 works with Azure Service Bus and all versions of Service Bus for Windows Server.

        private readonly HttpClient _httpClient;
        private readonly string _serviceNamespace;
        private readonly string _keyName;
        private readonly string _key;
        private readonly object _lock = new object();

        public ILogger Logger { get; set; }

        // Ignore root certificate store and accept all SSL certificates. Useful for IoT devices
        // with no certificate store to verify against (which will cause https requests to fail).
        // For more info see http://www.mono-project.com/archived/usingtrustedrootsrespectfully/
        public static bool DisableServerCertificateValidation { get; set; }

        // Create HttpClient object and get SAS token
        public HttpClientHelper(string serviceNamespace, string sasKeyName, string sasKey, int expirationInMinutes)
        {
            if (DisableServerCertificateValidation)
            {
                ServicePointManager.ServerCertificateValidationCallback = (object sender,
                    X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;
            }

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("ContentType", "application/atom+xml;type=entry;charset=utf-8");
            _serviceNamespace = serviceNamespace;
            _keyName = sasKeyName;
            _key = sasKey;
            RenewSasToken(expirationInMinutes);
        }

        // Create HttpClient object and get ACS token
        public HttpClientHelper(string serviceNamespace, string acsIdentity, string acsKey)
        {
            if (DisableServerCertificateValidation)
            {
                ServicePointManager.ServerCertificateValidationCallback = (object sender,
                    X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;
            }

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("ContentType", "application/atom+xml;type=entry;charset=utf-8");
            _serviceNamespace = serviceNamespace;
            _keyName = acsIdentity;
            _key = acsKey;
            RenewAcsToken();
        }

        // Renew the SAS token
        public void RenewSasToken(int expirationInMinutes)
        {
            lock (_lock)
            {
                var token = GetSasToken(_serviceNamespace, _keyName, _key, expirationInMinutes);
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);
            }
        }

        // Renew the ACS token
        public void RenewAcsToken()
        {
            lock (_lock)
            {
                var token = GetAcsToken(_serviceNamespace, _keyName, _key).Result;
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);
            }
        }

        // Create a SAS token. SAS tokens are described in http://msdn.microsoft.com/en-us/library/windowsazure/dn170477.aspx.
        private static string GetSasToken(string serviceNamespace, string keyName, string key, int expirationInMinutes)
        {
            // Set token lifetime to n minutes.
            var epochOrigin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            var epochTimeNow = DateTime.Now.ToUniversalTime() - epochOrigin;
            var tokenStartTime = Convert.ToUInt32(epochTimeNow.Subtract(TimeSpan.FromMinutes(15)).TotalSeconds); // set the start time to 15 minutes ago to account for any server time skew
            var tokenExpirationTime = Convert.ToUInt32(epochTimeNow.Add(TimeSpan.FromMinutes(expirationInMinutes)).TotalSeconds);

            var stringToSign = HttpUtility.UrlEncode(serviceNamespace) + "\n" + tokenExpirationTime;
            var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));

            var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            var token = String.Format(CultureInfo.InvariantCulture, "SharedAccessSignature sr={0}&sig={1}&st={2}&se={3}&skn={4}&sv=2014-02-14",
                HttpUtility.UrlEncode(serviceNamespace), HttpUtility.UrlEncode(signature), tokenStartTime, tokenExpirationTime, keyName);
            return token;
        }

        // Call ACS to get a token.
        private async Task<string> GetAcsToken(string serviceNamespace, string issuerName, string issuerSecret)
        {
            var postData = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("wrap_name", issuerName),
                new KeyValuePair<string, string>("wrap_password", issuerSecret),
                new KeyValuePair<string, string>("wrap_scope", "http://" + serviceNamespace + ".servicebus.windows.net/")
            };

            var postContent = new FormUrlEncodedContent(postData);
            var  response = await _httpClient.PostAsync("https://" + serviceNamespace + "-sb.accesscontrol.windows.net/WRAPv0.9/", postContent);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var responseProperties = responseBody.Split('&');
            var tokenProperty = responseProperties[0].Split('=');
            var token = Uri.UnescapeDataString(tokenProperty[1]);

            return string.Format("WRAP access_token=\"{0}\"", token);
        }

        // Get properties of an entity.
        public async Task<byte[]> GetEntity(string address, int timeoutInSeconds = 5)
        {
            HttpResponseMessage response = null;
            try
            {
                response = await _httpClient.GetAsync(address + "?timeout=" + timeoutInSeconds + ApiVersion);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException(ex.Message);
                }
                throw;
            }
        }

        // Create an entity.
        public async Task CreateEntity(string address, byte[] entityDescription, int timeoutInSeconds = 5)
        {
            HttpResponseMessage response = null;
            try
            {
                response = await _httpClient.PutAsync(address + "?timeout=" + timeoutInSeconds + ApiVersion, new ByteArrayContent(entityDescription));
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    switch ((int)response.StatusCode)
                    {
                        case 401:
                            throw new UnauthorizedAccessException(ex.Message);
                        case 409:
                            if (Logger != null)
                                Logger.Warning(typeof(HttpClientHelper), "Entity " + address + " already exists");
                            return;
                    }
                }
                throw;
            }
        }

        // Delete an entity.
        public async Task DeleteEntity(string address, int timeoutInSeconds = 5)
        {
            HttpResponseMessage response = null;
            try
            {
                response = await _httpClient.DeleteAsync(address + "?timeout=" + timeoutInSeconds + ApiVersion);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    switch ((int)response.StatusCode)
                    {
                        case 401:
                            throw new UnauthorizedAccessException(ex.Message);
                        case 404:
                            if (Logger != null)
                                Logger.Warning(typeof(HttpClientHelper), "Entity " + address + " not found");
                            return;
                    }
                }
                throw;
            }
        }

        // Send a message.
        public async Task SendMessage(string address, ServiceBusHttpMessage message, int timeoutInSeconds = 5)
        {
            var postContent = new ByteArrayContent(message.Body);

            // Serialize BrokerProperties.
            var serializer = new DataContractJsonSerializer(typeof(BrokerProperties));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, message.BrokerProperties);
                postContent.Headers.Add("BrokerProperties", Encoding.UTF8.GetString(ms.ToArray()));
            }

            // Add custom properties.
            foreach (string key in message.CustomProperties)
            {
                postContent.Headers.Add(key, message.CustomProperties.GetValues(key));
            }

            HttpResponseMessage response = null;
            try
            {
                // Send message.
                response = await _httpClient.PostAsync(address + "/messages" + "?timeout=" + timeoutInSeconds, postContent);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException(ex.Message);
                }
                throw;
            }
        }

        // Send a batch of messages.
        public async Task SendMessageBatch(string address, ServiceBusHttpMessage message, int timeoutInSeconds = 5)
        {
            // Custom properties that are defined for the brokered message that contains the batch are ignored.
            // Throw exception to signal that these properties are ignored.
            if (message.CustomProperties.Count != 0)
                throw new ArgumentException("Custom properties in BrokeredMessage are ignored.");

            var postContent = new ByteArrayContent(message.Body);
            postContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.microsoft.servicebus.json");

            HttpResponseMessage response = null;
            try
            {
                // Send message.
                response = await _httpClient.PostAsync(address + "/messages" + "?timeout=" + timeoutInSeconds, postContent);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException(ex.Message);
                }
                throw;
            }
        }

        // Peek and lock message. The parameter messageUri contains the URI of the message, which can be used to complete the message.
        public async Task<ServiceBusHttpMessage> ReceiveMessage(string address, int timeoutInSeconds = 60)
        {
            return await Receive(address, false, timeoutInSeconds);
        }

        // Receive and delete message.
        public async Task<ServiceBusHttpMessage> ReceiveAndDeleteMessage(string address, int timeoutInSeconds = 60)
        {
            return await Receive(address, true, timeoutInSeconds);
        }

        public async Task<ServiceBusHttpMessage> Receive(string address, bool deleteMessage, int timeoutInSeconds = 60)
        {
            HttpResponseMessage response = null;
            try
            {
                // Retrieve message from Service Bus.
                if (deleteMessage)
                    response = await _httpClient.DeleteAsync(address + "/messages/head?timeout=" + timeoutInSeconds);
                else
                    response = await _httpClient.PostAsync(address + "/messages/head?timeout=" + timeoutInSeconds, new ByteArrayContent(new byte[0]));
            
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException(ex.Message);
                }
                throw;
            }

            // Check if a "valid" message was returned.
            var headers = response.Headers;
            if (!headers.Contains("BrokerProperties"))
                return null;

            // Get message body.
            var message = new ServiceBusHttpMessage
            {
                Body = await response.Content.ReadAsByteArrayAsync()
            };

            // Deserialize BrokerProperties.
            var brokerProperties = headers.GetValues("BrokerProperties");
            var serializer = new DataContractJsonSerializer(typeof(BrokerProperties));
            foreach (var key in brokerProperties )
            {
                using (var ms = new MemoryStream(Encoding.ASCII.GetBytes(key)))
                {
                    message.BrokerProperties = (BrokerProperties)serializer.ReadObject(ms);
                }
            }

            // Get custom propoerties.
            foreach (var header in headers)
            {
                var key = header.Key;
                if (!key.Equals("Transfer-Encoding") && !key.Equals("BrokerProperties") && !key.Equals("ContentType") && !key.Equals("Location") && !key.Equals("Date") && !key.Equals("Server"))
                {
                    foreach (var value in header.Value)
                    {
                        var cleanValue = new string(value.Where(c => c != '\"').ToArray()); // strip out any quotes
                        message.CustomProperties.Add(key, cleanValue);
                    }
                }
            }

            // Get message URI.
            if (headers.Contains("Location"))
            {
                var locationProperties = headers.GetValues("Location");
                message.Location = locationProperties.FirstOrDefault();
            }
            return message;
        }

        // Delete message with the specified MessageId and LockToken.
        public async Task DeleteMessage(string address, string messageId, Guid lockId)
        {
            var messageUri = address + "/messages/" + messageId + "/" + lockId.ToString();
            await DeleteMessage(messageUri);
        }

        // Delete message with the specified SequenceNumber and LockToken
        public async Task DeleteMessage(string address, long seqNum, Guid lockId)
        {
            var messageUri = address + "/messages/" + seqNum + "/" + lockId.ToString();
            await DeleteMessage(messageUri);
        }

        // Delete message with the specified URI. The URI is returned in the Location header of the response of the Peek request.
        public async Task DeleteMessage(string messageUri, int timeoutInSeconds = 5)
        {
            HttpResponseMessage response = null;
            try
            {
                response = await _httpClient.DeleteAsync(messageUri + "?timeout=" + timeoutInSeconds);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException(ex.Message);
                }
                throw;
            }
        }

        // Unlock message with the specified MessageId and LockToken.
        public async Task UnlockMessage(string address, string messageId, Guid lockId)
        {
            var messageUri = address + "/messages/" + messageId + "/" + lockId.ToString();
            await UnlockMessage(messageUri);
        }

        // Unlock message with the specified SequenceNumber and LockToken
        public async Task UnlockMessage(string address, long seqNum, Guid lockId)
        {
            var messageUri = address + "/messages/" + seqNum + "/" + lockId.ToString();
            await UnlockMessage(messageUri);
        }

        // Unlock message with the specified URI. The URI is returned in the Location header of the response of the Peek request.
        public async Task UnlockMessage(string messageUri, int timeoutInSeconds = 5)
        {
            HttpResponseMessage response = null;
            try
            {
                response = await _httpClient.PutAsync(messageUri + "?timeout=" + timeoutInSeconds, null);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException(ex.Message);
                }
                throw;
            }
        }

        // Renew lock of the message with the specified MessageId and LockToken.
        public async Task RenewLock(string address, string messageId, Guid LockId)
        {
            var messageUri = address + "/messages/" + messageId + "/" + LockId.ToString();
            await RenewLock(messageUri);
        }

        // Renew lock of the message with the specified SequenceNumber and LockToken
        public async Task RenewLock(string address, long seqNum, Guid LockId)
        {
            var messageUri = address + "/messages/" + seqNum + "/" + LockId.ToString();
            await RenewLock(messageUri);
        }

        // Renew lock of the message with the specified URI. The URI is returned in the Location header of the response of the Peek request.
        public async Task RenewLock(string messageUri, int timeoutInSeconds = 5)
        {
            HttpResponseMessage response = null;
            try
            {
                response = await _httpClient.PostAsync(messageUri + "?timeout" + timeoutInSeconds, null);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                if (response != null)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new UnauthorizedAccessException(ex.Message);
                }
                throw;
            }
        }
    }
}
