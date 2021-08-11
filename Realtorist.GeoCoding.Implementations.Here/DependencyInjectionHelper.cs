using Geo.Here.Abstractions;
using Geo.Here.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Realtorist.GeoCoding.Abstractions;
using Realtorist.GeoCoding.Implementations.Here.Models;
using System;

namespace Realtorist.GeoCoding.Implementations.Here
{
    /// <summary>
    /// Provides dependency injection helper methods
    /// </summary>
    public static class DependencyInjectionHelper
    {
        /// <summary>
        /// Configures services related to geo coding using HERE APIs
        /// </summary>
        /// <param name="services">Services collection</param>
        public static void ConfigureHereGeoCoding(this IServiceCollection services)
        {
                services.AddHereServices(null);
                services.AddSingleton<IGeoCoder, HereGeoCoder>().AddHttpClient<HereGeoCoder>();
                services.Replace(new ServiceDescriptor(typeof(IHereKeyContainer), typeof(HereKeyFromSettingsContainer), ServiceLifetime.Singleton));
                services.AddHttpClient<IBatchGeoCoder, HereBatchGeoCoder>()
                    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(5, _ => TimeSpan.FromMilliseconds(600)));
        }
    }
}
