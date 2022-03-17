﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AudibleApi.Common;
using Dinah.Core;
using Dinah.Core.Net.Http;
using Newtonsoft.Json.Linq;

namespace AudibleApi
{
	public static class LibraryQueryStringBuilderExtensions
	{
		public static string ToQueryString(this LibraryOptions.ResponseGroupOptions responseGroupOptions)
		{
			if (responseGroupOptions == LibraryOptions.ResponseGroupOptions.None)
				return "";

			var descriptions = responseGroupOptions
				.ToValues()
				.Select(e => e.GetDescription())
				.ToList();
			if (!descriptions.Any() || descriptions.Any(d => d is null))
				throw new Exception("Unexpected value in response group");
			var str = "response_groups=" + descriptions.Aggregate((a, b) => $"{a},{b}");
			return str;
		}

		public static string ToQueryString(this LibraryOptions.SortByOptions sortByOptions)
		{
			if (sortByOptions == LibraryOptions.SortByOptions.None)
				return "";

			var description = sortByOptions.GetDescription();
			if (description is null)
				throw new Exception("Unexpected value for sort by");
			var str = "sort_by=" + description;
			return str;
		}

		public static string ToQueryString(this LibraryOptions libraryOptions)
		{
			var parameters = new List<string>();

			if (libraryOptions.NumberOfResultPerPage.HasValue)
				parameters.Add("num_results=" + libraryOptions.NumberOfResultPerPage.Value);

			if (libraryOptions.PageNumber.HasValue)
				parameters.Add("page=" + libraryOptions.PageNumber.Value);

			if (libraryOptions.PurchasedAfter.HasValue)
				parameters.Add("purchased_after=" + libraryOptions.PurchasedAfter.Value.ToRfc3339String());

			if (libraryOptions.ResponseGroups != LibraryOptions.ResponseGroupOptions.None)
				parameters.Add(libraryOptions.ResponseGroups.ToQueryString());

			if (libraryOptions.SortBy != LibraryOptions.SortByOptions.None)
				parameters.Add(libraryOptions.SortBy.ToQueryString());

			if (!parameters.Any())
				return "";

			return parameters.Aggregate((a, b) => $"{a}&{b}");
		}
	}
	public class LibraryOptions
	{
		public const int NUMBER_OF_RESULTS_PER_PAGE_MIN = 1;
		public const int NUMBER_OF_RESULTS_PER_PAGE_MAX = 1000;

		private int? _numResults;
		public int? NumberOfResultPerPage
		{
			get => _numResults;
            set => _numResults
				= value is null
				? null
				: ArgumentValidator.EnsureBetweenInclusive(value.Value, nameof(value), NUMBER_OF_RESULTS_PER_PAGE_MIN, NUMBER_OF_RESULTS_PER_PAGE_MAX);
		}

		private int? _page;
		public int? PageNumber
		{
			get => _page;
			set => _page
				= value is null
				? null
				: ArgumentValidator.EnsureGreaterThan(value.Value, nameof(value), 0);
		}

		public DateTime? PurchasedAfter { get; set; }

		[Flags]
		public enum ResponseGroupOptions
		{
			None = 0,
			[Description("badge_types")]
			BadgeTypes = 1 << 0,
			[Description("category_ladders")]
			CategoryLadders = 1 << 1,
			[Description("claim_code_url")]
			ClaimCodeUrl = 1 << 2,
			[Description("contributors")]
			Contributors = 1 << 3,
			[Description("is_downloaded")]
			IsDownloaded = 1 << 4,
			[Description("is_returnable")]
			IsReturnable = 1 << 5,
			[Description("media")]
			Media = 1 << 6,
			[Description("origin_asin")]
			OriginAsin = 1 << 7,
			[Description("pdf_url")]
			PdfUrl = 1 << 8,
			[Description("percent_complete")]
			PercentComplete = 1 << 9,
			[Description("price")]
			Price = 1 << 10,
			[Description("product_attrs")]
			ProductAttrs = 1 << 11,
			[Description("product_desc")]
			ProductDesc = 1 << 12,
			[Description("product_extended_attrs")]
			ProductExtendedAttrs = 1 << 13,
			[Description("product_plan_details")]
			ProductPlanDetails = 1 << 14,
			[Description("product_plans")]
			ProductPlans = 1 << 15,
			/// <summary>"provided_review" is null unless used with "rating"</summary>
			[Description("provided_review")]
			ProvidedReview = 1 << 16,
			[Description("rating")]
			Rating = 1 << 17,
			[Description("relationships")]
			Relationships = 1 << 18,
			[Description("review_attrs")]
			ReviewAttrs = 1 << 19,
			[Description("reviews")]
			Reviews = 1 << 20,
			[Description("sample")]
			Sample = 1 << 21,
			[Description("series")]
			Series = 1 << 22,
			[Description("sku")]
			Sku = 1 << 23,
			// https://stackoverflow.com/questions/7467722
			ALL_OPTIONS = ~(1 << 24)
		}
		public ResponseGroupOptions ResponseGroups { get; set; }

		public enum SortByOptions
		{
			None,
			[Description("-Author")]
			AuthorDesc,
			[Description("-Length")]
			LengthDesc,
			[Description("-Narrator")]
			NarratorDesc,
			[Description("-PurchaseDate")]
			PurchaseDateDesc,
			[Description("-Title")]
			TitleDesc,
			[Description("Author")]
			Author,
			[Description("Length")]
			Length,
			[Description("Narrator")]
			Narrator,
			[Description("PurchaseDate")]
			PurchaseDate,
			[Description("Title")]
			Title
		}
		public SortByOptions SortBy { get; set; }
	}

	public partial class Api
	{
		const string LIBRARY_PATH = "/1.0/library";

		#region GetLibraryAsync
		public Task<JObject> GetLibraryAsync()
			=> getLibraryAsync(new LibraryOptions { PurchasedAfter = new DateTime(1970, 1, 1) }.ToQueryString());

		public async Task<JObject> GetLibraryAsync(LibraryOptions libraryOptions)
		{
			if (libraryOptions is null)
				return await GetLibraryAsync();

			return await getLibraryAsync(libraryOptions.ToQueryString());
		}

		public async Task<JObject> GetLibraryAsync(string libraryOptions)
		{
			if (libraryOptions is null)
				return await GetLibraryAsync();

			libraryOptions = libraryOptions.Trim().Trim('?');

			if (string.IsNullOrWhiteSpace(libraryOptions))
				return await GetLibraryAsync();

			return await getLibraryAsync(libraryOptions);
		}

		// all strings passed here are assumed to be unconditionally valid
		private async Task<JObject> getLibraryAsync(string parameters)
		{
			var url = $"{LIBRARY_PATH}?{parameters}";
			var response = await AdHocAuthenticatedGetAsync(url);
			var obj = await response.Content.ReadAsJObjectAsync();
			return obj;
		}
		#endregion

		#region GetLibraryBookAsync
		public Task<Item> GetLibraryBookAsync(string asin, LibraryOptions.ResponseGroupOptions responseGroups)
			=> GetLibraryBookAsync(asin, responseGroups.ToQueryString());

		public async Task<Item> GetLibraryBookAsync(string asin, string responseGroups)
		{
			if (asin is null)
				throw new ArgumentNullException(nameof(asin));
			if (string.IsNullOrWhiteSpace(asin))
				throw new ArgumentException("asin may not be blank", nameof(asin));

			asin = asin.ToUpper().Trim();

			responseGroups = responseGroups?.Trim().Trim('?');

			var url = $"{LIBRARY_PATH}/{asin}";
			if (!string.IsNullOrWhiteSpace(responseGroups))
				url += "?" + responseGroups;
			var response = await AdHocAuthenticatedGetAsync(url);
			var obj = await response.Content.ReadAsJObjectAsync();
			var objStr = obj.ToString();

			BookDtoV10 dto;
			try
			{
				// important! use this convert/deser method
				dto = BookDtoV10.FromJson(objStr);
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, "Error converting catalog product. Full json:\r\n" + objStr);
				throw;
			}

			return dto.Item;
		}
		#endregion

		#region GetLibraryBookChapters
		public async Task<ContentMetadata> GetLibraryBookMetadataAsync(string asin)
		{
			if (asin is null)
				throw new ArgumentNullException(nameof(asin));
			if (string.IsNullOrWhiteSpace(asin))
				throw new ArgumentException("asin may not be blank", nameof(asin));

			asin = asin.ToUpper().Trim();

			var url = $"{CONTENT_PATH}/{asin}/metadata?response_groups=chapter_info,content_reference";
			var response = await AdHocAuthenticatedGetAsync(url);
			var bookJObj = await response.Content.ReadAsJObjectAsync();
			var metadataJson = bookJObj.ToString();

			MetadataDtoV10 contentMetadata;
			try
			{
				contentMetadata = MetadataDtoV10.FromJson(metadataJson);
			}
			catch (Exception ex)
			{
				Serilog.Log.Logger.Error(ex, $"Error retrieving content metadata for asin: {asin}");
				throw;
			}
			return contentMetadata?.ContentMetadata;
		}
		#endregion

		#region GetAllLibraryItemsAsync
		public async Task<List<Item>> GetAllLibraryItemsAsync() => await GetAllLibraryItemsAsync(LibraryOptions.ResponseGroupOptions.ALL_OPTIONS);
		public async Task<List<Item>> GetAllLibraryItemsAsync(LibraryOptions.ResponseGroupOptions responseGroups)
		{
			var libraryOptions = new LibraryOptions
			{
				PurchasedAfter = new DateTime(2000, 1, 1),
				ResponseGroups = responseGroups
			};

			try
			{
				// max 1000 however with higher numbers it stops returning 'provided_review' and 'reviews' groups.
				// Sometimes this threshold is as high as 900, sometimes as low as 400.
				// I've never had problems at 300. Another user had nearly constant problems at 300.
				libraryOptions.NumberOfResultPerPage = 250;
				return await getAllLibraryItemsAsync(libraryOptions);
			}
			catch (Exception ex) when (ex is TaskCanceledException || ex is TimeoutException)
			{
				// if it times out with 250, try 50. This takes about 5 seconds longer for a library of 1,100
				//
				// For each batch size, I ran 3 tests. Results in milliseconds
				//   1000    19389 , 17760 , 19256
				//    500    19099 , 19905 , 18553
				//    250    20288 , 19163 , 19605
				//    100    22156 , 22058 , 22438
				//     50    25017 , 24292 , 24491
				//     25    28627 , 30006 , 31201
				//     10    45006 , 46717 , 44924
				libraryOptions.NumberOfResultPerPage = 50;
				return await getAllLibraryItemsAsync(libraryOptions);
			}
			catch (Exception)
			{
				throw;
			}
		}
		public async Task<List<Item>> getAllLibraryItemsAsync(LibraryOptions libraryOptions)
		{
			var allItems = new List<Item>();

			for (var pageNumber = 1; ; pageNumber++)
			{
				// if attempting to paginate more than 10,000 titles : {"error_code":null,"message":"Implied library size is unsupported"}"
				if (pageNumber * libraryOptions.NumberOfResultPerPage > 10000)
				{
					Serilog.Log.Logger.Information($"Maximum reached. Cannot retrieve more than 10,000 titles");
					break;
				}

				libraryOptions.PageNumber = pageNumber;
				var page = await GetLibraryAsync(libraryOptions);

				var pageStr = page.ToString();

				LibraryDtoV10 libResult;
				try
				{
					// important! use this convert/deser method
					libResult = LibraryDtoV10.FromJson(pageStr);
				}
				catch (Exception ex)
				{
					Serilog.Log.Logger.Error(ex, "Error converting library for importing use. Full library:\r\n" + pageStr);
					throw;
				}

				if (!libResult.Items.Any())
					break;

				Serilog.Log.Logger.Information($"Page {pageNumber}: {libResult.Items.Length} results");
				allItems.AddRange(libResult.Items);
			}

			return allItems;
		}
		#endregion
	}
}
