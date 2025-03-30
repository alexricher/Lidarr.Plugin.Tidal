using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Plugin.Tidal;
using System;

namespace NzbDrone.Core.Indexers.Tidal
{
    public class TidalIndexerSettingsValidator : AbstractValidator<TidalIndexerSettings>
    {
        public TidalIndexerSettingsValidator()
        {
            RuleFor(x => x.ConfigPath).IsValidPath();
            
            // Add validation for the redirect URL
            RuleFor(x => x.RedirectUrl)
                .Must(BeValidRedirectUrl)
                .WithMessage("Redirect URL must be a valid https URL containing a 'code=' parameter to authenticate with Tidal");
            
            // Rate limiting validation
            RuleFor(x => x.MaxConcurrentSearches)
                .GreaterThan(0)
                .WithMessage("Maximum concurrent searches must be greater than 0");
                
            RuleFor(x => x.MaxRequestsPerMinute)
                .GreaterThan(0)
                .WithMessage("Maximum requests per minute must be greater than 0");
                
            RuleFor(x => x.MaxPages)
                .GreaterThan(0)
                .WithMessage("Maximum pages must be greater than 0");
                
            RuleFor(x => x.PageSize)
                .GreaterThan(0)
                .WithMessage("Page size must be greater than 0");
        }
        
        private bool BeValidRedirectUrl(string url)
        {
            // An empty URL is valid (although it won't allow login)
            if (string.IsNullOrEmpty(url))
                return true;
                
            try
            {
                // Verify URL format
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    return false;
                    
                // Verify it parses as a valid URI
                var uri = new Uri(url);
                
                // Verify it contains 'code=' parameter 
                return uri.Query.Contains("code=");
            }
            catch
            {
                return false;
            }
        }
    }

    public class TidalIndexerSettings : IIndexerSettings
    {
        private static readonly TidalIndexerSettingsValidator Validator = new TidalIndexerSettingsValidator();

        [FieldDefinition(0, Label = "Tidal URL", HelpText = "Use this to sign into Tidal.")]
        public string TidalUrl { get => TidalAPI.Instance?.Client?.GetPkceLoginUrl() ?? ""; set { } }

        [FieldDefinition(1, Label = "Redirect Url", Type = FieldType.Textbox)]
        public string RedirectUrl { get; set; } = "";

        [FieldDefinition(2, Label = "Config Path", Type = FieldType.Textbox, HelpText = "This is the directory where you account's information is stored so that it can be reloaded later.")]
        public string ConfigPath { get; set; } = "";

        [FieldDefinition(3, Type = FieldType.Number, Label = "Early Download Limit", Unit = "days", HelpText = "Time before release date Lidarr will download from this indexer, empty is no limit", Advanced = true)]
        public int? EarlyReleaseLimit { get; set; }

        // Rate limiting settings
        [FieldDefinition(4, Label = "Max Concurrent Searches", Type = FieldType.Number, HelpText = "Maximum number of concurrent searches allowed", Advanced = true)]
        public int MaxConcurrentSearches { get; set; } = 5;

        [FieldDefinition(5, Label = "Max Requests Per Minute", Type = FieldType.Number, HelpText = "Maximum number of API requests per minute to avoid rate limiting", Advanced = true)]
        public int MaxRequestsPerMinute { get; set; } = 50;

        [FieldDefinition(6, Label = "Max Pages Per Search", Type = FieldType.Number, HelpText = "Maximum number of pages to retrieve per search", Advanced = true)]
        public int MaxPages { get; set; } = 3;

        [FieldDefinition(7, Label = "Page Size", Type = FieldType.Number, HelpText = "Number of results per page", Advanced = true)]
        public int PageSize { get; set; } = 100;

        [FieldDefinition(8, Label = "Preferred Country Code", Type = FieldType.Textbox, HelpText = "Two-letter country code to use for API requests (e.g. US, GB, DE)", Advanced = true)]
        public string PreferredCountryCode { get; set; } = "US";

        // this is hardcoded so this doesn't need to exist except that it's required by the interface
        public string BaseUrl { get; set; } = "";

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
