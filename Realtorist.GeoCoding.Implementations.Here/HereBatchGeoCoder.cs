using Microsoft.Extensions.Logging;
using Realtorist.DataAccess.Abstractions;
using Realtorist.GeoCoding.Abstractions;
using Realtorist.Models.Helpers;
using Realtorist.Models.Listings.Details;
using Realtorist.Models.Geo;
using Realtorist.Models.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Realtorist.GeoCoding.Implementations.Here
{
    /// <summary>
    /// Implements batch geo coding using HERE APIs
    /// </summary>
    public class HereBatchGeoCoder : IBatchGeoCoder
    {
        private const char inputDelimeter = '|';
        private const char outputDelimeter = ',';

        private readonly ISettingsDataAccess _settingsDataAccess;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        /// <summary>
        /// Creates new instance of <see cref="HereBatchGeoCoder"/>
        /// </summary>
        /// <param name="settingsDataAccess">Settings provider</param>
        /// <param name="httpClient">HTTP client</param>
        /// <param name="logger">Logger</param>
        public HereBatchGeoCoder(ISettingsDataAccess settingsDataAccess, HttpClient httpClient, ILogger<HereBatchGeoCoder> logger)
        {
            _settingsDataAccess = settingsDataAccess ?? throw new ArgumentNullException(nameof(settingsDataAccess));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task GeoCodeAddressesAsync(IDictionary<Guid, Address> addresses, Func<Guid, Coordinates, Task> callback, Func<IEnumerable<Guid>, Task> failledCallback)
        {
            if (addresses is null) throw new ArgumentNullException(nameof(addresses)); 
            if (callback is null) throw new ArgumentNullException(nameof(callback)); 
            if (failledCallback is null) throw new ArgumentNullException(nameof(failledCallback));

            if (addresses.Count == 0) 
            {
                _logger.LogInformation("Zero addresses were suplied. Won't proceed.");
                return;
            };

            var settings = await _settingsDataAccess.GetSettingAsync<GeoCodingSettings>(SettingTypes.GeoCoding);
            var url = $"https://batch.geocoder.ls.hereapi.com/6.2/jobs?&apiKey={settings.HereApiKey}&action=run&header=true&inDelim={WebUtility.UrlEncode(inputDelimeter.ToString())}&outDelim={WebUtility.UrlEncode(outputDelimeter.ToString())}&outCols=latitude,longitude&outputcombined=true&language=en";

            _logger.LogInformation($"GeoCoding {addresses.Count} addresses");

            var input = GetInputData(addresses, inputDelimeter);
            var response = await _httpClient.PostAsync(url, new StringContent(input));
            response.EnsureSuccessStatusCode();

            var responseXml = await response.Content.ReadAsStringAsync();
            var xml = new XmlDocument();
            xml.LoadXml(responseXml);

            var jobId = xml.GetElementsByTagName("RequestId")[0].InnerText;
            _logger.LogInformation($"Job {jobId} was submitted and started");

            var waitResult = await WaitForCompletitionAsync(settings, jobId);
            if (!waitResult)
            {
                _logger.LogError($"Job {jobId} failed");
                return;
            }

            _logger.LogInformation($"Job {jobId} completed. Downloading result");
            var processed = await DownloadAndProcessResultAsync(settings, jobId, outputDelimeter, addresses, callback);
            var failed = addresses.Keys.Except(processed).ToArray();

            _logger.LogInformation($"Orignal number of requests: {addresses.Count}. Processed results: {processed.Count}. Failed count: {failed.Length}");
            _logger.LogInformation("Processing failed records...");

            await failledCallback(failed);

            _logger.LogInformation("Done.");
        }

        private async Task<bool> WaitForCompletitionAsync(GeoCodingSettings settings, string jobId)
        {
            if (jobId.IsNullOrEmpty()) throw new ArgumentException($"{nameof(jobId)} shouldn't be null or empty");
            
            do
            {
                _logger.LogInformation($"Waiting job {jobId} to complete");
                var url = $"https://batch.geocoder.ls.hereapi.com/6.2/jobs/{jobId}?&apiKey={settings.HereApiKey}&action=status";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var responseXml = await response.Content.ReadAsStringAsync();
                var xml = new XmlDocument();
                xml.LoadXml(responseXml);

                var status = xml.GetElementsByTagName("Status")[0].InnerText;
                if (status == "completed")
                {
                    _logger.LogInformation($"Job {jobId} completed successfully");
                    return true;
                }

                if (status == "failed" || status == "deleted" || status == "cancelled")
                {
                    _logger.LogInformation($"Job {jobId} finished but not completed: {status}");
                    return false;
                }

                _logger.LogInformation($"Job {jobId} is still in progress: {status}");
                await Task.Delay(TimeSpan.FromSeconds(5));
            } while (true);
        }

        private async Task<IList<Guid>> DownloadAndProcessResultAsync(GeoCodingSettings settings, string jobId, char delimeter, IDictionary<Guid, Address> map, Func<Guid, Coordinates, Task> processFunc)
        {
            _logger.LogInformation($"Downloading job {jobId} result");
            var url = $"https://batch.geocoder.ls.hereapi.com/6.2/jobs/{jobId}/result?&apiKey={settings.HereApiKey}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var zipStream = await response.Content.ReadAsStreamAsync();
            using var zip = new ZipArchive(zipStream);
            var file = zip.Entries.FirstOrDefault();
            if (file == null) throw new InvalidOperationException("Can't find file inside zip archive");

            using var fileStream = file.Open();
            using var reader = new StreamReader(fileStream);
            var header = reader.ReadLine();
            var processed = new List<Guid>();
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                var parts = line.Split(delimeter);
                if (parts.Length != 5) throw new InvalidOperationException($"Line '{line}' has wrong number of arguments");

                var id = Guid.Parse(parts[0]);
                if (!double.TryParse(parts[3], out var latitude) || !double.TryParse(parts[4], out var longitude))
                {
                    _logger.LogWarning($"Couldn't get coordinates for listing with ID {id} (Address: {map[id].ToJson()}: {line}");
                    continue;
                }

                await processFunc(id, new Coordinates(latitude, longitude));
                processed.Add(id);
            }

            _logger.LogInformation($"Processed {processed.Count} results");
            return processed;
        }

        private static string GetInputData(IDictionary<Guid, Address> addresses, char delimeter)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"recId{delimeter}street{delimeter}city{delimeter}state{delimeter}postalCode{delimeter}country");
            foreach (var pair in addresses)
            {
                sb.AppendLine($"{pair.Key}{delimeter}{pair.Value.StreetAddress}{delimeter}{pair.Value.City}{delimeter}{pair.Value.Province}{delimeter}{pair.Value.PostalCode}{delimeter}{Constants.CountryCode}");
            }

            return sb.ToString();
        }
    }
}
