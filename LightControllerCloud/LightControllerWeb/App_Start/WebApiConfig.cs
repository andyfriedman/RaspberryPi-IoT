using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace LightControllerWeb
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "LightsApi",
                routeTemplate: "api/{controller}/{lightSwitch}",
                defaults: new { lightSwitch = "*" }
            );

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
