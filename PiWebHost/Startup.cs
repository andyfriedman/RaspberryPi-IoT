using Owin;

namespace PiWebHost
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.UseWelcomePage("/");
            app.UseNancy();
        }
    }
}
