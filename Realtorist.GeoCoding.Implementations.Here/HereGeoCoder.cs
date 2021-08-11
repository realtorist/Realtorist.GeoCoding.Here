using Geo.Here.Abstractions;
using Newtonsoft.Json;
using Realtorist.DataAccess.Abstractions;
using Realtorist.GeoCoding.Abstractions;
using Realtorist.GeoCoding.Implementations.Here.Models;
using Realtorist.Models.Helpers;
using Realtorist.Models.Listings.Details;
using Realtorist.Models.Models;
using Realtorist.Models.Settings;
using Realtorist.Services.Abstractions.Cache;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Realtorist.GeoCoding.Implementations.Here
{
    /// <summary>
    /// Implements geocoder which uses HERE APIs
    /// </summary>
    public partial class HereGeoCoder : IGeoCoder
    {
        private readonly IHereGeocoding _hereGeocoding;
        private readonly ISettingsDataAccess _settingsDataAccess;
        private readonly HttpClient _httpClient;

        private readonly ICache<string, string[]> _cachedSuggestions;
        private readonly ICache<string, Coordinates> _cachedCoordinates;

        public HereGeoCoder(IHereGeocoding hereGeocoding, ICacheFactory cacheFactory, ISettingsDataAccess settingsDataAccess, HttpClient httpClient)
        {
            _hereGeocoding = hereGeocoding ?? throw new ArgumentNullException(nameof(hereGeocoding));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _settingsDataAccess = settingsDataAccess ?? throw new ArgumentNullException(nameof(settingsDataAccess));

            if (cacheFactory is null) throw new ArgumentNullException(nameof(httpClient));

            _cachedCoordinates = cacheFactory.GetCache<string, Coordinates>(Constants.InMemoryCacheCapacity);
            _cachedSuggestions = cacheFactory.GetCache<string, string[]>(Constants.InMemoryCacheCapacity);
        }

        public async Task<Address> GetAddressAsync(Coordinates coordinates)
        {
            if (coordinates.IsNullOrEmpty()) throw new ArgumentException($"Coordinates shouldn't be {(coordinates is null ? "null" : "empty")}");

            var response = await _hereGeocoding.ReverseGeocodingAsync(new Geo.Here.Models.Parameters.ReverseGeocodeParameters
            {
                At = new Geo.Here.Models.Coordinate
                {
                    Latitude = coordinates.Latitude,
                    Longitude = coordinates.Longitude
                }
            });

            var selected = response.Items.FirstOrDefault();
            if (selected is null) return null;
            return new Address
            {
                Country = selected.Address.CountryName,
                City = selected.Address.City,
                CommunityName = selected.Address.Block,
                Neighbourhood = selected.Address.District,
                PostalCode = selected.Address.PostalCode,
                Province = selected.Address.State,
                StreetName = selected.Address.Street,
                StreetNumber = selected.Address.HouseNumber,
                Subdivision = selected.Address.SubDistrict,
                Coordinates = new Coordinates
                {
                    Latitude = selected.Position.Latitude,
                    Longitude = selected.Position.Longitude,
                },
                StreetAddress = $"{selected.Address.HouseNumber} {selected.Address.Street}"
            };
        }

        public async Task<string[]> GetAddressSuggestionsAsync(string query)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));

            if (_cachedSuggestions.ContainsKey(query)) return _cachedSuggestions[query];

            var settings = await _settingsDataAccess.GetSettingAsync<GeoCodingSettings>(SettingTypes.GeoCoding);

            var response = await _httpClient.GetAsync($"https://autocomplete.search.hereapi.com/v1/autocomplete?apiKey={settings.HereApiKey}&q={WebUtility.UrlEncode(query)}&limit=5&in=countryCode:{Constants.CountryCode}");
            response.EnsureSuccessStatusCode();

            var json = JsonConvert.DeserializeObject<HereAutoCompleteResponse>(await response.Content.ReadAsStringAsync());

            var results = json.Items?.Select(x => x.Address.Label).ToArray() ?? new string[0];
            _cachedSuggestions[query] = results;

            return results;
        }

        public async Task<Coordinates> GetCoordinatesAsync(Address address)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));

            var result = await _hereGeocoding.GeocodingAsync(new Geo.Here.Models.Parameters.GeocodeParameters
            {
                QualifiedQuery = $"houseNumber={address.StreetNumber};street={address.StreetName} {address.StreetSuffix};state={address.Province};country={address.Country};postalCode={address.PostalCode}"
            });

            var geo = result.Items.FirstOrDefault();
            if (geo == null) return null;

            return new Coordinates
            {
                Latitude = geo.Position.Latitude,
                Longitude = geo.Position.Longitude
            };
        }

        public async Task<Coordinates> GetCoordinatesAsync(string query)
        {
            if (query is null) throw new ArgumentNullException(nameof(query));

            if (_cachedCoordinates.ContainsKey(query)) return _cachedCoordinates[query];

            var result = await _hereGeocoding.GeocodingAsync(new Geo.Here.Models.Parameters.GeocodeParameters
            {
                Query = query
            });

            var geo = result.Items.FirstOrDefault();
            if (geo == null) return null;

            var coordinates =  new Coordinates
            {
                Latitude = geo.Position.Latitude,
                Longitude = geo.Position.Longitude
            };

            _cachedCoordinates[query] = coordinates;
            return coordinates;
        }
    }
}
