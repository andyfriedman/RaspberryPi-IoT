using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Threading;

namespace IpAddressSender
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var smtpHost = ConfigurationManager.AppSettings["SmtpHost"];
                var smtpUserName = ConfigurationManager.AppSettings["SmtpUserName"];
                var smtpPassword = ConfigurationManager.AppSettings["SmtpPassword"];
                var senderAddress = ConfigurationManager.AppSettings["SenderAddress"];
                var recipientAddress = ConfigurationManager.AppSettings["RecipientAddress"];

                // get the local host name and ip address
                var deviceId = Dns.GetHostName();
                IPAddress ip = null;
                var localhost = IPAddress.Parse("127.0.0.1");

                Console.WriteLine("Waiting for IP address...");

                do
                {
                    ip = Dns.GetHostAddresses(deviceId)
                        .First(x => x.AddressFamily == AddressFamily.InterNetwork);
                } while (ip.Equals(localhost));

                Console.WriteLine(deviceId + ": " + ip);

                // create message
                var message = new MailMessage
                {
                    From = new MailAddress(senderAddress),
                    Body = string.Format("{0}: {1}\r\nhttp://{1}:9999/logs", deviceId, ip)
                };
                message.To.Add(new MailAddress(recipientAddress));

                // send email
                var smtp = new SmtpClient(smtpHost)
                {
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Credentials = new NetworkCredential(smtpUserName, smtpPassword)
                };

                // retry up to 3 times 
                Exception lastError = null;
                for (var i=0; i<3; i++)
                {
                    try
                    {
                        smtp.Send(message);
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Thread.Sleep(5000);
                    }
                }

                if (lastError != null)
                    throw lastError;

                Console.WriteLine("IP address sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
