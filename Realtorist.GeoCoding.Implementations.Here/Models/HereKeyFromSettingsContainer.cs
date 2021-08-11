using System;
using Geo.Here.Abstractions;
using Realtorist.DataAccess.Abstractions;
using Realtorist.Models.Settings;

namespace Realtorist.GeoCoding.Implementations.Here.Models
{
    internal class HereKeyFromSettingsContainer : IHereKeyContainer
    {
        private readonly ISettingsDataAccess _settingsDataAccess;

        public HereKeyFromSettingsContainer(ISettingsDataAccess settingsDataAccess)
        {
            _settingsDataAccess = settingsDataAccess ?? throw new ArgumentNullException(nameof(settingsDataAccess));
        }

        public string GetKey()
        {
            var settings = _settingsDataAccess.GetSettingAsync<GeoCodingSettings>(SettingTypes.GeoCoding).Result;
            return settings.HereApiKey;
        }
    }
}
