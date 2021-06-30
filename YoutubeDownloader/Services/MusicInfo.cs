using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Net.Http;
using DiscogsConnect.Http;
using System.Threading;
using DiscogsConnect;
using RestClient = RestSharp.RestClient;

namespace YoutubeDownloader.Services
{
    public class RateLimitHandler : HttpClientHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            var rateLimit = RateLimit.Parse(response);

            Console.WriteLine($"Used:{rateLimit.Total} - Remaining:{rateLimit.Remaining} - Total:{rateLimit.Total}");

            if (rateLimit.Remaining < 2)
                Thread.Sleep(TimeSpan.FromSeconds(90));

            return response;
        }
    }

    public static class MusicInfo
    {
        private static DiscogsClient discogsClient;
        public static bool InitializeDiscog(string apikey, string app)
        {
            try
            {
                var options = new DiscogsOptions
                {
                    Token = apikey,
                    UserAgent = app
                };

                var httpClient = new HttpClient(new RateLimitHandler(), true);
                discogsClient = new DiscogsClient(options, httpClient);
            }
            catch
            {
                return false;
            }

            return true;
        }

        public static Task<PaginationResponse<SearchResult>> Search(SearchCriteria search)
        {
            return discogsClient.Database.SearchAsync(search);
        }

        public static Task<Artist> GetArtist(int artistid)
        {
            return discogsClient.Database.GetArtistAsync(artistid);
        }

        public static Task<PaginationResponse<ArtistRelease>> GetArtistReleases(int releaseid, int page = 1, int resultsinpage = 100)
        {
            return discogsClient.Database.GetArtistReleasesAsync(releaseid, page, resultsinpage);
        }

        public static Task<Release> GetRelease(int releaseid, Currency currency = Currency.EUR)
        {
            return discogsClient.Database.GetReleaseAsync(releaseid, currency);
        }
    }

    public static class ShazamMusicInfo
    {
        public static bool TryExtractArtistAndTitle(
            string videoTitle,
            List<string> shazamapikeys,
            List<string> vagalumeapikeys,
            out string artist,
            out string title,
            out string picturelink,
            out string track)
        {
            foreach (var key in shazamapikeys)
            {
                var json = GetShazaminfo(videoTitle, key);
                if (!string.IsNullOrEmpty(json))
                {
                    var data = JsonConvert.DeserializeObject<Root>(json);
                    if (data.tracks != null)
                    {
                        artist = data.tracks.hits.FirstOrDefault()?.track.subtitle;
                        title = data.tracks.hits.FirstOrDefault()?.track.title;
                        picturelink = data.tracks.hits.FirstOrDefault()?.track.images.coverarthq;
                        track = null;
                        return true;
                    }
                }
            }
            foreach (var key in vagalumeapikeys)
            {
                var json = Getvagalumeinfo(videoTitle, key);
                if (!string.IsNullOrEmpty(json))
                {
                    var data = JsonConvert.DeserializeObject<vagalume>(json);
                    if (data?.response?.docs != null && data?.response?.docs?.Count > 0)
                    {
                        var infofound = data.response.docs.OrderBy(t => string.IsNullOrEmpty(t.title)).ThenByDescending(t => t?.fmRadios?.Count ?? 0).FirstOrDefault();
                        artist = infofound.band;
                        title = infofound.title;
                        picturelink = null;
                        track = null;
                        return true;
                    }
                }
            }
            artist = null;
            title = null;
            picturelink = null;
            track = null;
            return false;
        }

        public static string GetShazaminfo(string name, string apikey)
        {
            apikey = apikey.Trim();
            string search = System.Net.WebUtility.HtmlEncode(name);
            var client = new RestClient("https://shazam.p.rapidapi.com/search?term=" + search + "&locale=en-US&offset=0&limit=20");
            var request = new RestRequest(Method.GET);
            request.AddHeader("x-rapidapi-key", apikey);
            request.AddHeader("x-rapidapi-host", "shazam.p.rapidapi.com");
            IRestResponse response = client.Execute(request);
            if (response.IsSuccessful)
                return response.Content;
            else return null;
        }

        public static string Getvagalumeinfo(string name, string apikey)
        {
            apikey = apikey.Trim();
            string search = System.Net.WebUtility.HtmlEncode(name);
            var client = new RestClient($"https://api.vagalume.com.br/search.artmus?apikey={apikey}&q={search}&limit=20");
            var request = new RestRequest(Method.GET);
            IRestResponse response = client.Execute(request);
            if (response.IsSuccessful)
                return response.Content;
            else return null;
        }

        // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
        public class Share
        {
            public string subject { get; set; }
            public string text { get; set; }
            public string href { get; set; }
            public string image { get; set; }
            public string twitter { get; set; }
            public string html { get; set; }
            public string avatar { get; set; }
            public string snapchat { get; set; }
        }

        public class Images
        {
            public string background { get; set; }
            public string coverart { get; set; }
            public string coverarthq { get; set; }
            public string joecolor { get; set; }
            public string overflow { get; set; }
            public string @default { get; set; }
        }

        public class Action
        {
            public string name { get; set; }
            public string type { get; set; }
            public string id { get; set; }
            public string uri { get; set; }
        }

        public class Beacondata
        {
            public string type { get; set; }
            public string providername { get; set; }
        }

        public class Option
        {
            public string caption { get; set; }
            public List<Action> actions { get; set; }
            public Beacondata beacondata { get; set; }
            public string image { get; set; }
            public string type { get; set; }
            public string listcaption { get; set; }
            public string overflowimage { get; set; }
            public bool colouroverflowimage { get; set; }
            public string providername { get; set; }
        }

        public class Provider
        {
            public string caption { get; set; }
            public Images images { get; set; }
            public List<Action> actions { get; set; }
            public string type { get; set; }
        }

        public class Hub
        {
            public string type { get; set; }
            public string image { get; set; }
            public List<Action> actions { get; set; }
            public List<Option> options { get; set; }
            public List<Provider> providers { get; set; }
            public bool @explicit { get; set; }
            public string displayname { get; set; }
        }

        public class Artist
        {
            public string id { get; set; }
            public string adamid { get; set; }
            public List<Hit> hits { get; set; }
        }

        public class Track
        {
            public string layout { get; set; }
            public string type { get; set; }
            public string key { get; set; }
            public string title { get; set; }
            public string subtitle { get; set; }
            public Share share { get; set; }
            public Images images { get; set; }
            public Hub hub { get; set; }
            public List<Artist> artists { get; set; }
            public string url { get; set; }
        }

        public class Hit
        {
            public Track track { get; set; }
            public Artist artist { get; set; }
        }

        public class Tracks
        {
            public List<Hit> hits { get; set; }
        }

        public class Artists
        {
            public List<Artist2> hits { get; set; }
        }

        public class Artist2
        {
            public string avatar { get; set; }
            public string id { get; set; }
            public string name { get; set; }
            public bool verified { get; set; }
            public string adamid { get; set; }
        }

        public class Root
        {
            public Tracks tracks { get; set; }
            public Artists artists { get; set; }
        }

        // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse); 
        public class Doc
        {
            public string id { get; set; }
            public string url { get; set; }
            public string band { get; set; }
            public int? langID { get; set; }
            public string title { get; set; }
            public List<string> fmRadios { get; set; }
        }
        public class Response
        {
            public int numFound { get; set; }
            public int start { get; set; }
            public bool numFoundExact { get; set; }
            public List<Doc> docs { get; set; }
        }

        public class Highlighting
        {

        }

        public class vagalume
        {
            public Response response { get; set; }
            public Highlighting highlighting { get; set; }
        }
                
    }
    public static class LevenshteinDistance
    {
        /// <summary>
        ///     Calculate the difference between 2 strings using the Levenshtein distance algorithm
        /// </summary>
        /// <param name="source1">First string</param>
        /// <param name="source2">Second string</param>
        /// <returns></returns>
        public static int Calculate(string source1, string source2, bool log = false) //O(n*m)
        {
            int? reversesearch = null;
            if (source2.Contains(" - "))
            {
                int temp;
                string s = source2;
                string newstring = "";
                string[] a = s.Split(" - ", StringSplitOptions.RemoveEmptyEntries);
                int k = a.Length - 1;
                temp = k;
                for (; temp >= 0; k--)
                {
                    if (!string.IsNullOrWhiteSpace(newstring))
                        newstring += " - ";
                    newstring += a[temp] + "";
                    --temp;
                }

                var reversesource1Length = source1.Length;
                var reversesource2Length = newstring.Length;

                var reversematrix = new int[reversesource1Length + 1, reversesource2Length + 1];

                // First calculation, if one entry is empty return full length
                if (reversesource1Length == 0)
                    return reversesource2Length;

                if (reversesource2Length == 0)
                    return reversesource1Length;

                // Initialization of matrix with row size source1Length and columns size source2Length
                for (var i = 0; i <= reversesource1Length; reversematrix[i, 0] = i++) { }
                for (var j = 0; j <= reversesource2Length; reversematrix[0, j] = j++) { }

                // Calculate rows and collumns distances
                for (var i = 1; i <= reversesource1Length; i++)
                {
                    for (var j = 1; j <= reversesource2Length; j++)
                    {
                        var cost = (newstring[j - 1] == source1[i - 1]) ? 0 : 1;

                        reversematrix[i, j] = Math.Min(
                            Math.Min(reversematrix[i - 1, j] + 1, reversematrix[i, j - 1] + 1),
                            reversematrix[i - 1, j - 1] + cost);
                    }
                }

                reversesearch = reversematrix[reversesource1Length, reversesource2Length];
                double lengthoffile1 = source1.Length;

                if (log)
                    Debug.WriteLine($"{source1} to {newstring}  {reversesearch} difference {((lengthoffile1 - reversesearch) / lengthoffile1) * 100} % correct");
            }

            var source1Length = source1.Length;
            var source2Length = source2.Length;

            var matrix = new int[source1Length + 1, source2Length + 1];

            // First calculation, if one entry is empty return full length
            if (source1Length == 0)
                return source2Length;

            if (source2Length == 0)
                return source1Length;

            // Initialization of matrix with row size source1Length and columns size source2Length
            for (var i = 0; i <= source1Length; matrix[i, 0] = i++) { }
            for (var j = 0; j <= source2Length; matrix[0, j] = j++) { }

            // Calculate rows and collumns distances
            for (var i = 1; i <= source1Length; i++)
            {
                for (var j = 1; j <= source2Length; j++)
                {
                    var cost = (source2[j - 1] == source1[i - 1]) ? 0 : 1;

                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            var value = matrix[source1Length, source2Length];

            if (log)
            {
                double lengthoffile1 = source1.Length;
                Debug.WriteLine($"{source1} to {source2}  {value} difference {((lengthoffile1 - value) / lengthoffile1) * 100} % correct");
            }

            // return result
            return reversesearch.HasValue && reversesearch < value ? reversesearch.Value : value;
        }

        public static double CalculatePercentage(string source1, string source2, bool log = false) //O(n*m)
        {
            int value = Calculate(source1, source2, log);

            double lengthoffile1 = source1.Length;
            double percdifference = ((lengthoffile1 - value) / lengthoffile1) * 100;

            // return result
            return percdifference;
        }
    }
}
