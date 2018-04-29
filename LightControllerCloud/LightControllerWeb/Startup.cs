using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(LightControllerWeb.Startup))]
namespace LightControllerWeb
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
