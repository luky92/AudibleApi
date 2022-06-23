﻿using AudibleApi.Common;
using Dinah.Core;
using Dinah.Core.Net.Http;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AudibleApi
{
    public enum DownloadQuality
    {
        Extreme,
        High,
        Normal,
        Low
    }
    public partial class Api
    {
        #region Download License

        /// <summary>
        /// Requests a license to download Audible content.
        /// </summary>
        /// <param name="asin">Audible Asin of book</param>
        /// <param name="quality">Desired audio Quality</param>
        /// <returns>a valid <see cref="ContentLicense"/> containing content_reference, chapter_info, and pdf_url.</returns>
        /// <exception cref="ApiErrorException">Thrown when the Api request failed.</exception>
        /// <exception cref="InvalidResponseException">Thrown when the Api did not return a proper <see cref="ContentLicense"/>.</exception>
        /// <exception cref="InvalidValueException">Thrown when <see cref="ContentLicense.StatusCode"/> is not "Granted" or "Denied".</exception>
        /// <exception cref="ValidationErrorException">Thrown when <see cref="ContentLicense.StatusCode"/> is "Denied".</exception>
        public async Task<ContentLicense> GetDownloadLicenseAsync(string asin, DownloadQuality quality = DownloadQuality.Extreme)
        {
            ArgumentValidator.EnsureNotNullOrWhiteSpace(asin, nameof(asin));

            var body = new JObject
            {
                { "consumption_type", "Download" },
                { "supported_drm_types", new JArray{ "Adrm", "Mpeg" } },
                { "quality", quality.ToString() },
                { "response_groups", "last_position_heard,pdf_url,content_reference,chapter_info"}
            };

            string requestUri = $"{CONTENT_PATH}/{asin}/licenserequest";
            HttpResponseMessage response;
            try
            {
                response = await AdHocAuthenticatedRequestAsync(requestUri, HttpMethod.Post, Client, body);
            }
            catch (ApiErrorException ex)
            {
                //Assume this exception will not contain PII.
                Serilog.Log.Logger.Error(ex, "Error getting download license");
                throw;
            }
            catch (Exception ex)
            {
                var apiExp = new ApiErrorException(
                    new Uri(requestUri),
                    body,
                    $"Error requesting license for asin: [{asin}]",
                    ex);
                Serilog.Log.Logger.Error(apiExp, "Error requesting download license");
                throw apiExp;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                var ex = new ApiErrorException(
                    response.Headers.Location,
                    //Assume this response does not contain PII.
                    new JObject { { "http_response_code", response.StatusCode.ToString() }, { "response", await response.Content.ReadAsStringAsync() } },
                    $"License response not \"OK\" for asin: [{asin}]");
                Serilog.Log.Logger.Error(ex, "Download response does not contain a valid status code");
                throw ex;
            }

            var responseJobj = await response.Content.ReadAsJObjectAsync();

            ContentLicenseDtoV10 contentLicenseDtoV10;
            try
            {
                contentLicenseDtoV10 = ContentLicenseDtoV10.FromJson(responseJobj, _identityMaintainer.DeviceType, _identityMaintainer.DeviceSerialNumber, _identityMaintainer.AmazonAccountId);
            }
            catch (Exception ex)
            {
                var apiExp = new InvalidResponseException(
                    response.Headers.Location,
                    responseJobj, //Even if the object doesn't parse, it may contain PII.
                    $"License response could not be parsed for asin: [{asin}]",
                    ex);
                Serilog.Log.Logger.Verbose(apiExp, "License response could not be parsed");
                throw apiExp;
            }

            if (contentLicenseDtoV10?.Message is not null)
            {
                var ex = new InvalidResponseException(
                    response.Headers.Location,
                    new JObject { { "message", contentLicenseDtoV10.Message } }, //Assume this message does not contain PII.
                    $"License response returned error for asin: [{asin}]");
                Serilog.Log.Logger.Error(ex, "License response returned error");
                throw ex;
            }

            if (contentLicenseDtoV10?.ContentLicense?.StatusCode is null)
            {
                var ex = new InvalidValueException(
                    response.Headers.Location,
                    responseJobj, //This error shouldn't happen, so log the entire response which contains PII.
                    $"License response does not contain a valid status code for asin: [{asin}]");
                Serilog.Log.Logger.Verbose(ex, "License response does not contain a valid status code");
                throw ex;
            }

            if (contentLicenseDtoV10.ContentLicense.StatusCode.EqualsInsensitive("Denied"))
            {
                var ex = new ValidationErrorException(
                    response.Headers.Location,
                    //Denial reasons may contain PII.
                    new JObject { { "license_denial_reasons", JArray.FromObject(contentLicenseDtoV10.ContentLicense.LicenseDenialReasons) } },
                    $"Content License denied for asin: [{asin}]");
                Serilog.Log.Logger.Verbose(ex, "Content License denied");
                throw ex;
            }

            if (!contentLicenseDtoV10.ContentLicense.StatusCode.EqualsInsensitive("Granted"))
            {
                var ex = new InvalidValueException(
                    response.Headers.Location,
                    responseJobj, //This error shouldn't happen, so log the entire response which contains PII.
                    $"Unrecognized status code \"{contentLicenseDtoV10.ContentLicense.StatusCode}\" for asin: [{asin}]");
                Serilog.Log.Logger.Verbose(ex, "Unrecognized status code");
                throw ex;
            }

            return contentLicenseDtoV10.ContentLicense;
        }

        #endregion

        public async Task<string> GetPdfDownloadLinkAsync(string asin)
        {
            //// SEPT 2020:

            //// no longer works. This url now returns 403
            // var downloadUrl = libraryBook?.Book?.Supplements?.FirstOrDefault()?.Url;

            //// this works for now:
            // MUST use relative url here
            var client = Sharer.GetSharedHttpClient(Locale.AudibleLoginUri());
            var response = await AdHocAuthenticatedRequestAsync($"/companion-file/{asin}", HttpMethod.Head, client);

            validatePdfDownloadUrl(asin, response);

            var downloadUrl = response.Headers.Location.AbsoluteUri;
            return downloadUrl;
        }

        // keep the brackets, the key, the colon, and the first and last 2 char.s.
        // Assumes value is 4+ characters long
        private System.Text.RegularExpressions.Regex adpTokenRegex = new("{([a-z]+:..).*?(..)}", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        // same as above but for "device-token"
        private System.Text.RegularExpressions.Regex deviceTokenRegex = new("(device-token=..).*?(..;)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private string regexReplacement = "$1...$2";
        private void validatePdfDownloadUrl(string asin, HttpResponseMessage response)
        {
            var body = $"\r\nASIN:{asin}\r\nLocale:{Locale}";

            if (response is null)
                throw new HttpRequestException("Response is null." + body);

            body += $"\r\nStatus Code:{(int)response.StatusCode} - {response.StatusCode}";

            // mask for log output
            // original:
            //   device-token=aZ/aZ/aZ; Domain=.audible.com; Expires=Wed, 22-Jun-2022 17:14:43 GMT; Path=/; Secure; HttpOnly, adpToken="{enc:a1Z+/=}{key:a1Z+/==}{iv:a1Z+/==}{name:AAZ1}{serial:Mg==}"; Domain=.audible.com;
            // result:
            // device-token=aZ...aZ; Domain=.audible.com; Expires=Wed, 22-Jun-2022 17:14:43 GMT; Path=/; Secure; HttpOnly, adpToken="{enc:a1.../=}{key:a1...==}{iv:a1...==}{name:AA...Z1}{serial:Mg...==}"; Domain=.audible.com;
            body = adpTokenRegex.Replace(body, regexReplacement);
            body = deviceTokenRegex.Replace(body, regexReplacement);

            if (response.Headers is null)
                throw new HttpRequestException("Response Headers are null." + body);

            if (response.Headers.Location is null)
                throw new HttpRequestException("Response Location is null." + body + $"\r\n{response.Headers}");
        }
    }
}
