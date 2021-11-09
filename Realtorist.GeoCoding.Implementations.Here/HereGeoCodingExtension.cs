using Geo.Here.Abstractions;
using Geo.Here.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly;
using Realtorist.Extensions.Base;
using Realtorist.Extensions.Base.Helpers;
using Realtorist.GeoCoding.Abstractions;
using Realtorist.GeoCoding.Implementations.Here.Models;
using System;

namespace Realtorist.GeoCoding.Implementations.Here
{
    /// <summary>
    /// Provides an extension for geo coder using HERE maps
    /// </summary>
    public class HereGeoCodingExtension : IConfigureServicesExtension
    {
        public int Priority => (int)ExtensionPriority.RegisterDefaultImplementations;

        public void ConfigureServices(IServiceCollection services, IServiceProvider serviceProvider)
        {
            services.AddHereServices(null);
            services.AddSingletonServiceIfNotRegisteredYet<IGeoCoder, HereGeoCoder>().AddHttpClient<HereGeoCoder>();
            services.Replace(new ServiceDescriptor(typeof(IHereKeyContainer), typeof(HereKeyFromSettingsContainer), ServiceLifetime.Singleton));
            services.AddSingletonServiceIfNotRegisteredYet<IBatchGeoCoder, HereBatchGeoCoder>()
                .AddHttpClient<HereBatchGeoCoder>()
                .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(5, _ => TimeSpan.FromMilliseconds(600)));
        }
    }
}
