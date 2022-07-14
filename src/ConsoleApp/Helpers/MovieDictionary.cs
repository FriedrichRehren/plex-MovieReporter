using PlexMovieReporter.Models;

namespace PlexMovieReporter.Helpers;

internal static class MovieDictionary
{
    internal static Dictionary<Resolution, string> MovieResolutionNames
    {
        get
        {
            return new Dictionary<Resolution, string>()
            {
                [Resolution.PAL] = "DVD - PAL (576p)",
                [Resolution.HD] = "BluRay - HD (720p)",
                [Resolution.FullHD] = "BluRay - FullHD (1080p)",
                [Resolution.QuadHD] = "BluRay - QuadHD (1440p)",
                [Resolution.UHDTV4K] = "BluRay - 4K (2160p)",
                [Resolution.UHDTV8K] = "BluRay - 8K (4320p)"
            };
        }
    }
}